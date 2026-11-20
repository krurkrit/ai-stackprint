// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class HealthPendingTests
{
    private static AppState State(bool projectsDone)
    {
        var s = new AppState { Health = [new HealthIssue("Safety", "skill-a", "claude-code", new Finding("downloads and runs code.", true))] };
        if (projectsDone)
        {
            s.ProjectHealth = [new HealthIssue("Projects", "dx1", "claude-code", new Finding("CLAUDE.md is over the limit.", true)), new HealthIssue("Projects", "dx1", "", new Finding("Rule never loads.", false))];
        }

        return s;
    }

    [Fact]
    public void Health_counts_the_scan_and_the_projects_once_both_are_in()
    {
        Assert.False(State(false).HealthReady);
        var s = State(true);
        Assert.True(s.HealthReady);
        Assert.Equal(3, s.AllHealth.Count());
        Assert.Equal(2, s.AllHealth.Count(h => h.Finding.Warning));
        Assert.Single(State(false).AllHealth);   // before: only the scan's own
    }

    [AvaloniaFact]
    public void The_dashboard_shows_loading_on_the_Health_tile_until_the_projects_are_checked_and_then_the_count()
    {
        var w = new Window
        {
            Width = 1300,
            Height = 900
        };
        var s = State(false);
        var x = new Ctx(s, w, _ => { }, () => { });
        w.Content = new ScrollViewer { Content = DashboardView.Build(x) };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Checking projects…", Ui.Texts(w));

        s.ProjectHealth = State(true).ProjectHealth;
        w.Content = new ScrollViewer { Content = DashboardView.Build(x) };
        Dispatcher.UIThread.RunJobs();
        var t = Ui.Texts(w);
        Assert.DoesNotContain("Checking projects…", t);
        Assert.Contains("2", t);   // warnings: one from the scan, one from a project
    }

    [AvaloniaFact]
    public void The_Health_page_says_the_projects_are_being_checked_and_lists_their_findings_when_done()
    {
        var w = new Window
        {
            Width = 1300,
            Height = 900
        };
        var s = State(false);
        var x = new Ctx(s, w, _ => { }, () => { });
        var view = new HealthView(x);
        w.Content = view.Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Checking projects…", Ui.Texts(w));
        Assert.Contains("skill-a", Ui.Texts(w));   // what the scan found is already there

        s.ProjectHealth = State(true).ProjectHealth;
        w.Content = view.Build();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Checking projects…", Ui.Texts(w));
        Assert.Contains("dx1", Ui.Texts(w));
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Control>(), c => c.Classes.Contains(Busy.Class) && c.IsEffectivelyVisible);
    }
}
