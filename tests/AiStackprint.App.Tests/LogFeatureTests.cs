// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Conversation: changed files, a Copy button on each step, an edit shown as a diff. Lines: a chip per level hides or shows those rows.</summary>
public sealed class LogFeatureTests
{
    private static IEnumerable<ReadOnlyMemory<byte>> Lines(params string[] lines) => lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));

    [AvaloniaFact]
    public void A_session_that_edited_files_says_how_many_and_each_step_has_a_Copy_button()
    {
        var session = new ClaudeSessionParser().Parse(Lines(
            """{"type":"user","timestamp":"2026-11-11T04:59:00Z","message":{"role":"user","content":"fix it"}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","message":{"id":"m1","model":"m","content":[{"type":"text","text":"done"},{"type":"tool_use","id":"a","name":"Edit","input":{"file_path":"src/A.cs","old_string":"1","new_string":"2"}}]}}"""));
        var log = new ReadableLog(session, false);
        var w = Ui.Show(log.Root);
        Assert.Contains("1 file changed", Ui.Texts(w));
        var copies = w.GetVisualDescendants().OfType<Button>().Where(b => ToolTip.GetTip(b) as string == "Copy this step as Markdown").ToList();
        Assert.Equal(session.Entries.Count, copies.Count);
        w.Close();
    }

    [AvaloniaFact]
    public void A_level_chip_hides_and_shows_the_lines_of_that_level()
    {
        var session = LineLogs.Parse(Lines(
            "2026-08-07 10:00:00 [info] started ok",
            "2026-08-07 10:00:01 [error] it broke",
            "2026-08-07 10:00:02 [info] went on"));
        var log = new ReadableLog(session, false);
        var w = Ui.Show(log.Root);
        bool Shown(string text) => w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);
        var chip = w.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Content as string == "Error 1");
        Assert.Contains(w.GetVisualDescendants().OfType<ToggleButton>(), b => b.Content as string == "Info 2");
        Assert.True(Shown("it broke"));
        chip.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown("it broke"));
        Assert.True(Shown("started ok"));
        chip.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Shown("it broke"));
        w.Close();
    }

    private static string Line(int n, string who, string text, string? agent = null) =>
        who == "user"
            ? "{\"type\":\"user\",\"timestamp\":\"2026-11-11T05:00:" + n.ToString("00") + "Z\",\"message\":{\"role\":\"user\",\"content\":\"" + text + "\"}}"
            : "{\"type\":\"assistant\",\"timestamp\":\"2026-11-11T05:00:" + n.ToString("00") + "Z\"" + (agent == null ? "" : ",\"isSidechain\":true,\"agentId\":\"" + agent + "\"")
              + ",\"message\":{\"id\":\"m" + n + "\",\"model\":\"m\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}}";

    private static Session WithAgents() => new ClaudeSessionParser().Parse(Lines(
        Line(1, "user", "Fix the login redirect loop"), Line(2, "assistant", "I will explore with an agent first."),
        Line(3, "assistant", "Agent 1: grep redirect", "a1"), Line(4, "assistant", "Agent 1: read middleware", "a1"), Line(5, "assistant", "Agent 1: found the old cookie", "a1"),
        Line(6, "assistant", "Run the tests and a review in parallel."),
        Line(7, "assistant", "Agent 2: npm test", "b2"), Line(8, "assistant", "Agent 3: read auth.ts", "c3"), Line(9, "assistant", "Agent 2: 2 tests failed", "b2"), Line(10, "assistant", "Agent 3: looks fine", "c3"), Line(11, "assistant", "Agent 2: edit the test", "b2"),
        Line(12, "assistant", "Both agents are back: tests pass."), Line(13, "user", "Thanks, ship it")));

    [AvaloniaFact]
    public void Sub_agents_branch_in_a_graph_beside_the_steps()
    {
        var session = WithAgents();
        var log = new ReadableLog(session, false);
        var w = Ui.Show(log.Root);
        w.Height = 1500;
        Dispatcher.UIThread.RunJobs();
        var cells = w.GetVisualDescendants().OfType<AgentGraphCell>().ToList();
        Assert.Equal(session.Entries.Count, cells.Count);   // one graph cell per step
        Assert.Equal(8, cells.Count(c => ToolTip.GetTip(c) is string));   // the sub-agents' steps name their agent on hover
        Assert.Contains(cells, c => ToolTip.GetTip(c) as string == "Agent 3 · c3");
        var png = Path.Combine(Path.GetTempPath(), "stackprint-agent-graph.png");
#pragma warning disable CS0618 // Save(path) writes a PNG (as the screenshot tests do)
        w.CaptureRenderedFrame()?.Save(png);
#pragma warning restore CS0618
        w.Close();
    }
}
