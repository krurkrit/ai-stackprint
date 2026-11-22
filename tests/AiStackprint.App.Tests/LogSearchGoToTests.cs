// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>A search result goes to its step in the view you are in (Conversation, Compact) and tints it, in the open file and in another one.</summary>
public sealed class LogSearchGoToTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("sp-goto-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private static string Msg(string role, int second, string text, string? agent = null) =>
        "{\"type\":\"" + role + "\",\"timestamp\":\"2026-11-11T05:00:" + second.ToString("00") + "Z\"" + (agent == null ? "" : ",\"isSidechain\":true,\"agentId\":\"" + agent + "\"") + ",\"message\":{\"id\":\"m" + second + "\",\"role\":\"" + role + "\",\"content\":\"" + text + "\"}}";

    private Window Page()
    {
        for (var i = 0; i < 2; i++)
        {
            var f = Path.Combine(_tmp, $"s{i}.jsonl");
            File.WriteAllLines(f, Enumerable.Range(0, 30).Select(n => Msg(n % 2 == 0 ? "user" : "assistant", n, n == 20 ? $"needle{i} here" : $"filler {i} {n}")));
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-i));
        }

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
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("s0.jsonl", StringComparison.Ordinal)) && !Ui.Texts(w).Contains("Reading log folders…") && Ui.Texts(w).Any(t => t.Contains("filler", StringComparison.Ordinal)));
        return w;
    }

    private static void Search(Window w, string text, bool everyFile)
    {
        w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true).Text = text;
        if (everyFile)
        {
            w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.IsEffectivelyVisible && ToolTipText(t).StartsWith("On: search every log file", StringComparison.Ordinal)).IsChecked = true;
        }

        w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Content as string == "Aa").IsChecked = true;   // searches at once
        Ui.WaitUntil(() => Results(w).Count > 0);
    }

    private static string ToolTipText(Control c) => ToolTip.GetTip(c) as string ?? "";

    /// <summary>The result rows in the side panel (they are the hand-cursor rows inside the pop-up wide panel).</summary>
    private static List<Border> Results(Window w) =>
        [.. w.GetVisualDescendants().OfType<Border>().Where(b => Math.Abs(b.Width - Widths.Popup) < 0.5).SelectMany(p => p.GetVisualDescendants().OfType<Border>()).Where(b => b.Cursor == W.Hand)];

    private static int Tints(Window w) => w.GetVisualDescendants().OfType<Border>().Count(b => !b.IsHitTestVisible && b.Parent is Grid);

    private static void ClickFirstResult(Window w)
    {
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var row = Results(w)[0];
        var at = row.TranslatePoint(new Point(4, 4), w)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(w, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(w, at, Avalonia.Input.MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_result_in_the_open_file_is_tinted(bool compact, bool everyFile)
    {
        var w = Page();
        if (compact)
        {
            Ui.ClickText(w, "Compact");
            Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("filler 0", StringComparison.Ordinal)));
        }

        Search(w, "needle0", everyFile);
        var before = Tints(w);
        ClickFirstResult(w);
        Ui.WaitUntil(() => Tints(w) > before);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_result_in_another_file_opens_it_and_is_tinted(bool compact)
    {
        var w = Page();
        if (compact)
        {
            Ui.ClickText(w, "Compact");
            Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("filler 0", StringComparison.Ordinal)));
        }

        Search(w, "needle1", true);
        var before = Tints(w);
        ClickFirstResult(w);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("filler 1", StringComparison.Ordinal)));
        Ui.WaitUntil(() => Tints(w) > before);
        w.Close();
    }

    [AvaloniaFact]
    public void A_result_older_than_the_steps_shown_loads_the_steps_around_it_and_is_tinted()
    {
        var f = Path.Combine(_tmp, "long.jsonl");
        File.WriteAllLines(f, Enumerable.Range(0, 700).Select(n => Msg(n % 2 == 0 ? "user" : "assistant", n % 60, n == 10 ? "oldneedle here" : $"filler long {n}")));
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(10));   // the newest: it is the file that opens
        var w = Page();
        Search(w, "oldneedle", true);
        var before = Tints(w);
        ClickFirstResult(w);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("Showing the part of the file around the result", StringComparison.Ordinal)));
        Ui.WaitUntil(() => Tints(w) > before);
        Assert.Contains(Ui.Texts(w), t => t.Contains("oldneedle", StringComparison.Ordinal));   // the step is built and shown
        w.Close();
    }

    [AvaloniaFact]
    public void A_result_in_a_sub_agents_file_goes_to_its_step_and_is_tinted()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "s0", "subagents"));
        File.WriteAllLines(Path.Combine(_tmp, "s0", "subagents", "agent-a1.jsonl"), [Msg("assistant", 10, "agentneedle in the agent", "a1")]);
        var w = Page();
        Search(w, "agentneedle", true);
        Assert.Contains(Ui.Texts(w), t => t.StartsWith("Sub-agent · Line 1", StringComparison.Ordinal));   // the result says it is in an agent's file, and which line
        var before = Tints(w);
        ClickFirstResult(w);
        Ui.WaitUntil(() => Tints(w) > before);
        w.Close();
    }
}
