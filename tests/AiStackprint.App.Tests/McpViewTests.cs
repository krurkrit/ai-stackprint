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

public sealed class McpViewTests
{
    [AvaloniaFact]
    public void Servers_are_listed_highest_level_first_and_the_Type_and_Status_menus_narrow_the_list()
    {
        var s = new AppState
        {
            Mcp =
            [
                new McpServer("fs-server", "npx -y fs@1.0.0", "/u/.claude.json", null, false)
                {
                    Apps = ["claude-code"],
                    Level = "User"
                },
                new McpServer("db-server", "uvx db", "/r/.mcp.json", "Not pinned: db", true)
                {
                    Apps = ["claude-code"],
                    Level = "Project",
                    Project = "/r"
                },
                new McpServer("gopls", "gopls serve", "/p/.lsp.json", null, false)
                {
                    Apps = ["claude-code"],
                    Level = "User",
                    Kind = "LSP",
                    Plugin = "go-tools@market"
                },
            ]
        };
        var w = new Window
        {
            Width = 1200,
            Height = 800
        };
        McpView? view = null;
        var x = new Ctx(s, w, _ => { }, () => { }) { Refresh = () => w.Content = view!.Build() };
        view = new McpView(x);
        w.Content = view.Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Ui.Texts(w);
        Assert.True(texts.IndexOf("db-server") < texts.IndexOf("fs-server"), "Project comes before User");
        Assert.Contains("Not pinned: db", texts);
        Assert.Contains("from plugin go-tools", texts);

        Assert.False(Ui.FilterMenu(w, "App").IsVisible);   // one app: nothing to choose
        Ui.FilterOnly(w, "Type", "LSP");
        Assert.Equal(["gopls"], new[] { "gopls", "db-server", "fs-server" }.Where(Ui.Texts(w).Contains));
        Assert.Contains("Shown: 1 of 3", Ui.Summary(w));

        Ui.FilterOnly(w, "Type", "MCP");
        Ui.FilterOnly(w, "Status", "Warning");
        Assert.DoesNotContain("fs-server", Ui.Texts(w));
        Assert.Contains("db-server", Ui.Texts(w));
    }
}
