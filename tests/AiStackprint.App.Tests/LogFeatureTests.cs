// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
}
