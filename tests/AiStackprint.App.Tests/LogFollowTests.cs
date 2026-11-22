// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Follow: what a session adds is read in the background and its steps go at the end of the view; the steps shown and the search results stay.</summary>
public sealed class LogFollowTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("sp-follow-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private string File0 => Path.Combine(_tmp, "s0.jsonl");

    private static string Msg(string role, int second, string text) =>
        "{\"type\":\"" + role + "\",\"timestamp\":\"2026-11-11T05:00:" + second.ToString("00") + "Z\",\"message\":{\"id\":\"m" + second + "\",\"role\":\"" + role + "\",\"content\":\"" + text + "\"}}";

    private Window Page()
    {
        File.WriteAllLines(File0, Enumerable.Range(0, 10).Select(n => Msg(n % 2 == 0 ? "user" : "assistant", n, n == 3 ? "first needle" : $"filler {n}")));
        var def = new AppDefinition
        {
            Id = "app",
            Name = "Test app",
            Category = "cli",
            Logs = [new LogSource { Path = _tmp, Pattern = "*.jsonl", Format = "claude-session" }]
        };
        var s = new AppState { Defs = [def], Apps = [new AppInfo("app", "Test app", "cli", null, true, null)] };
        var w = new Window { Width = 1200, Height = 800 };
        w.Content = new LogsView(new Ctx(s, w, _ => { }, () => { })).Build("app");
        w.Show();
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("filler 9", StringComparison.Ordinal)));
        w.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.OnContent as string == "Follow newest").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static TextBlock Shown(Window w, string text) =>
        w.GetVisualDescendants().OfType<TextBlock>().First(t => (t.Inlines is { Count: > 0 } i ? string.Concat(i.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text ?? "").Contains(text, StringComparison.Ordinal));

    /// <summary>The result rows in the side panel.</summary>
    private static List<Border> Results(Window w) =>
        [.. w.GetVisualDescendants().OfType<Border>().Where(b => Math.Abs(b.Width - Widths.Popup) < 0.5).SelectMany(p => p.GetVisualDescendants().OfType<Border>()).Where(b => b.Cursor == W.Hand)];

    private static int Tints(Window w) => w.GetVisualDescendants().OfType<Border>().Count(b => !b.IsHitTestVisible && b.Parent is Panel);

    [AvaloniaFact]
    public void New_steps_go_at_the_end_and_are_tinted_without_building_the_others_again()
    {
        var w = Page();
        var old = Shown(w, "filler 5");
        var before = Tints(w);
        File.AppendAllLines(File0, [Msg("assistant", 20, "a new reply")]);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("a new reply", StringComparison.Ordinal)), 15000);
        Assert.Same(old, Shown(w, "filler 5"));   // the steps already shown are the same controls: only the new one was built
        Assert.True(Tints(w) > before);
        w.Close();
    }

    [AvaloniaFact]
    public void A_match_in_the_new_steps_is_added_to_the_results_without_searching_again()
    {
        var w = Page();
        w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true).Text = "needle";
        w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Content as string == "Aa").IsChecked = true;
        for (var i = 0; i < 30; i++)   // the typed text waits on a timer: let every search finish
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Ui.WaitUntil(() => Ui.Texts(w).Contains("1 step"));
        var first = Results(w)[0];
        File.AppendAllLines(File0, [Msg("assistant", 21, "second needle")]);
        Ui.WaitUntil(() => Ui.Texts(w).Contains("2 steps"), 15000);
        Assert.Equal(2, Results(w).Count);
        Assert.Same(first, Results(w)[0]);   // the first result row is the same: the results were added to, not searched again
        w.Close();
    }
}
