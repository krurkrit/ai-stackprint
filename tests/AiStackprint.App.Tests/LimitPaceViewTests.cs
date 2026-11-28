// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The weekly pace on the Plan &amp; limits card: the summary lines for each case and the chart, drawn (and under the pointer) in both themes.</summary>
public sealed class LimitPaceViewTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aipace-").FullName;
    private static readonly string? _out = Environment.GetEnvironmentVariable("STACKPRINT_SHOTS");
    private static readonly AppDefinition _claude = new()
    {
        Id = "claude-code",
        Name = "Claude Code"
    };

    public void Dispose()
    {
        Theme.Appearance(Application.Current!, SettingsStore.Defaults());
        Directory.Delete(_tmp, true);
    }

    /// <summary>A week with <paramref name="hoursLeft"/> hours left (3 days 9 hours) and a 5-hour window that started 3 hours ago; <paramref name="weekly"/> and <paramref name="five"/> are the readings of the last 4 hours, the last one now.</summary>
    private (Ctx Ctx, LimitSnapshot Now) Week(double dayBefore, double[] weekly, double[] five, int hoursLeft = 81)
    {
        var now = DateTime.Now;
        DateTime resetWeek = now.AddHours(hoursLeft), resetFive = now.AddHours(2);
        var s = new AppState();
        var x = new Ctx(s, new Window(), _ => { }, () => { }) { Limits = new LimitsService(() => s.Settings, new LimitHistory(Path.Combine(_tmp, Guid.NewGuid() + ".json"))) };
        LimitReading At(DateTime t, double w, double? f) => new(t, [.. new[] { f is { } v ? new LimitWindow("5-hour", v, resetFive) : null, new LimitWindow("Weekly", w, resetWeek) }.OfType<LimitWindow>()]);
        x.Limits.History.Add(_claude.Id, At(now.AddHours(-24), dayBefore, null), now);
        for (var i = 0; i < weekly.Length - 1; i++)
        {
            x.Limits.History.Add(_claude.Id, At(now.AddHours(i - weekly.Length + 1), weekly[i], five[i]), now);
        }

        var last = At(now, weekly[^1], five[^1]);
        return (x, new LimitSnapshot("Max", last.Windows, now));
    }

    [AvaloniaTheory]
    [InlineData("hot", 40, new[] { 60.0, 62, 64, 66 }, new[] { 0.0, 25, 50, 75 }, new[] { "% over the even pace", "Over its pace: full about", "Limit ", "before the reset" })]
    [InlineData("on", 45, new[] { 50.0, 50.5, 51, 51.5 }, new[] { 0.0, 2, 4, 6 }, new[] { "On the even pace", "Under its pace", "Lasts to the reset" })]
    [InlineData("low", 18, new[] { 19.0, 19.5, 20, 20.5 }, new[] { 0.0, 2, 4, 6 }, new[] { "% under the even pace", "Under its pace", "% left", "Unused at the reset" })]
    [InlineData("end", 80, new[] { 88.0, 89, 90, 91 }, new[] { 0.0, 2, 4, 6 }, new[] { "% under the even pace", "Under its pace", "Lasts to the reset" }, 1)]   // an hour before the reset: "Reset" keeps clear of the 5-hour name
    public void The_summary_says_where_use_stands_and_the_chart_draws_in_both_themes(string name, double dayBefore, double[] weekly, double[] five, string[] says, int hoursLeft = 81)
    {
        var (x, now) = Week(dayBefore, weekly, five, hoursLeft);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var settings = SettingsStore.Defaults();
            settings.Theme = theme;
            Theme.Appearance(Application.Current!, settings);
            var pace = LimitsView.Pace(x, _claude, now)!;
            var w = Ui.Show(new Border { Child = pace, Padding = new Thickness(Space.XXL) }.Themed(Border.BackgroundProperty, W.Surface));
            var texts = string.Join("\n", Ui.Texts(w));
            Assert.Contains("Weekly limit", texts);
            Assert.Contains("At this pace", texts);
            Assert.All(says, s => Assert.Contains(s, texts));
            Assert.Equal(name == "hot", texts.Contains("5-hour pace (current window)", StringComparison.Ordinal));   // the line shows once the 5-hour share is known

            var chart = w.GetVisualDescendants().OfType<PaceChart>().Single();
            Assert.True(chart.Bounds.Width > 0);
            chart.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, chart, new Pointer(1, PointerType.Mouse, true), w, new Point(chart.Bounds.Width * 0.3, 100), 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.NotNull(w.CaptureRenderedFrame());   // draws the lines and the hover box
            chart.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, chart, new Pointer(1, PointerType.Mouse, true), w, new Point(chart.Bounds.Width * 0.75, 100), 0, new PointerPointProperties(), KeyModifiers.None));
            var frame = w.CaptureRenderedFrame();   // after now (but "end"): the forecast; clear of "Reset" at the right end
            Assert.NotNull(frame);
            if (_out != null)
            {
                Directory.CreateDirectory(_out);
#pragma warning disable CS0618 // Save(path) writes a PNG; see ScreenshotTests.Shot
                frame.Save(Path.Combine(_out, $"feature-limit-pace{(name == "hot" ? "" : "-" + name)}-{theme.ToLowerInvariant()}.png"));
#pragma warning restore CS0618
            }

            chart.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, chart, new Pointer(1, PointerType.Mouse, true), w, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            w.Close();
        }
    }

    [AvaloniaFact]
    public void No_window_with_a_length_and_a_reset_shows_no_pace()
    {
        var s = new AppState();
        var x = new Ctx(s, new Window(), _ => { }, () => { }) { Limits = new LimitsService(() => s.Settings, new LimitHistory(Path.Combine(_tmp, "h.json"))) };
        Assert.Null(LimitsView.Pace(x, _claude, new LimitSnapshot(null, [new("Premium requests", 40, DateTime.Now.AddDays(3))], DateTime.Now)));
    }
}
