// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class HooksViewTests
{
    [AvaloniaFact]
    public void Hooks_are_listed_highest_level_first_with_their_plugin_and_the_Status_menu_narrows_the_list()
    {
        var s = new AppState
        {
            Hooks =
            [
                new Hook("Stop", null, "echo done", "command", "/u/.claude/settings.json")
                {
                    Apps = ["claude-code"],
                    Level = "User"
                },
                new Hook("afterFileEdit", null, "bash ./gone.sh", "command", "/r/.cursor/hooks.json")
                    {
                        Apps = ["cursor"],
                        Level = "Project",
                        Project = "/r",
                        Missing = "/r/gone.sh",
                        Note = "runs /r/gone.sh, which does not exist",
                        Warn = true
                    },
                new Hook("PreToolUse", "Bash", "bash ${CLAUDE_PLUGIN_ROOT}/guard.sh", "command", "/p/hooks/hooks.json")
                {
                    Apps = ["claude-code"],
                    Level = "User",
                    Plugin = "guard@market"
                },
            ]
        };
        var w = new Window
        {
            Width = 1200,
            Height = 800
        };
        HooksView? view = null;
        var x = new Ctx(s, w, _ => { }, () => { }) { Refresh = () => w.Content = view!.Build() };
        view = new HooksView(x);
        w.Content = view.Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Ui.Texts(w);
        Assert.True(texts.IndexOf("AfterFileEdit") < texts.IndexOf("Stop"), "Project comes before User");
        Assert.Contains("Bash", texts);
        Assert.Contains("Every", texts);
        Assert.Contains("from plugin guard", texts);
        Assert.Contains("runs /r/gone.sh, which does not exist", texts);

        Ui.FilterOnly(w, "Status", "Warning");
        Assert.Equal(["AfterFileEdit"], new[] { "AfterFileEdit", "Stop", "PreToolUse" }.Where(Ui.Texts(w).Contains));
    }

    [AvaloniaFact]
    public void Events_that_differ_only_in_the_first_letter_are_one_event_shown_with_a_capital()
    {
        var s = new AppState
        {
            Hooks =
            [
                new Hook("stop", null, "echo a", "command", "/r/.cursor/hooks.json") { Apps = ["cursor"] },
                new Hook("Stop", null, "echo b", "command", "/u/.claude/settings.json") { Apps = ["claude-code"] },
                new Hook("start", null, "echo c", "command", "/u/.claude/settings.json") { Apps = ["claude-code"] }
            ]
        };
        var w = new Window { Width = 1200, Height = 800 };
        var x = new Ctx(s, w, _ => { }, () => { });
        w.Content = new HooksView(x).Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Ui.Texts(w);
        Assert.Equal(2, texts.Count(t => t == "Stop"));   // two rows
        Assert.DoesNotContain("stop", texts);
        Assert.Contains("Start", texts);

        Ui.FilterOnly(w, "Event", "Stop");
        texts = Ui.Texts(w);
        Assert.Equal(2, texts.Count(t => t == "Stop"));
        Assert.DoesNotContain("Start", texts);
    }

    [AvaloniaTheory]
    [InlineData("Claude Code")]
    [InlineData("Gemini CLI")]
    [InlineData("Cursor")]
    [InlineData("Copilot")]
    [InlineData("Antigravity")]
    [InlineData("Kiro")]
    public void The_event_dialog_shows_the_whole_diagram_of_every_app_in_a_screen_that_is_not_tall(string app)
    {
        var w = Dialogs.HookEventsWindow([]);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Ui.Click(Ui.Button(w, app));
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Assert.True(w.Bounds.Height <= 800, $"{app}: {w.Bounds.Height}");
    }

    [AvaloniaFact]
    public void The_event_diagram_opens_on_the_app_with_most_hooks_marks_its_events_and_the_picker_switches_app()
    {
        var hooks = new[]
        {
            new Hook("afterFileEdit", null, "echo a", "command", "/r/.cursor/hooks.json") { Apps = ["cursor"] },
            new Hook("afterFileEdit", null, "echo b", "command", "/u/.cursor/hooks.json") { Apps = ["cursor"] },
            new Hook("Stop", null, "echo c", "command", "/u/.claude/settings.json") { Apps = ["claude-code"] }
        };
        var w = Ui.Show(new HookFlow(hooks));

        var texts = Ui.Texts(w);
        Assert.Contains("BeforeShellExecution", texts);   // Cursor's diagram
        Assert.Contains("2", texts);
        Assert.DoesNotContain("PostToolBatch", texts);

        Ui.Click(Ui.Button(w, "Claude Code"));
        Dispatcher.UIThread.RunJobs();
        texts = Ui.Texts(w);
        Assert.Contains("PostToolBatch", texts);
        Assert.Contains("1", texts);
        Assert.DoesNotContain("2", texts);
    }
}
