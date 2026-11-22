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
        Assert.True(texts.IndexOf("afterFileEdit") < texts.IndexOf("Stop"), "Project comes before User");
        Assert.Contains("Bash", texts);
        Assert.Contains("Every", texts);
        Assert.Contains("from plugin guard", texts);
        Assert.Contains("runs /r/gone.sh, which does not exist", texts);

        Ui.FilterOnly(w, "Status", "Warning");
        Assert.Equal(["afterFileEdit"], new[] { "afterFileEdit", "Stop", "PreToolUse" }.Where(Ui.Texts(w).Contains));
    }
}
