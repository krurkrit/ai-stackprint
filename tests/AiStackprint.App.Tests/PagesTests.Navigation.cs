// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace AiStackprint.App.Tests;

public sealed partial class PagesTests
{
    [AvaloniaFact]
    public void Skills_count_returns_to_the_total_when_every_value_is_on_again_and_the_menu_counts_follow_the_level_filter()
    {
        var (w, x) = Host(State());
        var skills = new LibraryView(x, "Skill");
        w.Content = skills.Build();
        Settle();
        Assert.EndsWith("Shown: 3", Ui.Summary(w));   // under the tool bar: all skills shown
        Assert.False(Ui.FilterMenu(w, "App").IsVisible);   // one app: nothing to choose

        Ui.FilterOnly(w, "Invoked by", "Agent only");
        Assert.EndsWith("Shown: 1 of 3", Ui.Summary(w));
        Assert.Contains("Invoked by: Agent only", Ui.Summary(w));

        foreach (var b in Ui.FilterBoxes(w, "Invoked by"))
        {
            b.IsChecked = true;   // every value on again: the total
        }

        Ui.FilterMenu(w, "Invoked by").Flyout!.Hide();
        Settle();
        Assert.EndsWith("Shown: 3", Ui.Summary(w));

        Ui.FilterOnly(w, "Level", "User");
        Assert.EndsWith("Shown: 2 of 3", Ui.Summary(w));
        var counts = Ui.FilterBoxes(w, "Invoked by").Select(b => int.Parse(Ui.Texts(b)[^1], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(2, counts.Sum());   // the counts follow the level filter
        Ui.FilterMenu(w, "Invoked by").Flyout!.Hide();
        skills.Leave();
        Show(w, skills.Build());
        Assert.Contains("Level: User", Ui.Summary(w));   // kept for the next visit

        Ui.Click(w.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Reset the search, filters and grouping"));
        Settle();
        Assert.EndsWith("Shown: 3", Ui.Summary(w));
        w.Close();
        Settle();
    }

    [AvaloniaFact]
    public void A_left_page_is_freed_and_the_next_visit_keeps_the_search_and_filters()
    {
        var (w, x) = Host(State());
        var skills = new LibraryView(x, "Skill");
        var (page, box) = FirstVisit(w, skills);

        w.Content = new TextBlock { Text = "another page" };   // what MainWindow does when another page opens
        skills.Leave();
        Free(w);
        Assert.False(page.IsAlive);   // nothing the view keeps holds the old page
        Assert.False(box.IsAlive);

        Show(w, skills.Build());
        Assert.Equal("review", SearchBox(w).Text);
        Assert.EndsWith("Shown: 1 of 3", Ui.Summary(w));
        SearchBox(w).Text = "";   // the search cleared: the filter and grouping set before it still apply
        Settle();
        Show(w, skills.Build());
        Assert.Contains("Group by: Invoked by", Ui.Summary(w));
        Assert.EndsWith("Shown: 3", Ui.Summary(w));
        w.Close();
        Settle();
    }

    /// <summary>The Skills page with a search and a filter; only weak references come back, so the test itself keeps nothing of it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Page, WeakReference Box) FirstVisit(Window w, LibraryView skills)
    {
        var page = Show(w, skills.Build());   // no filter menu is opened: the headless mouse, shared by the tests, can keep pointing into a closed menu's list
        Ui.Click(Ui.Button(w, "Invoked by"));   // Group by Invoked by
        Settle();
        SearchBox(w).Text = "review";   // kept for the next visit (headless timers do not run, so it searches when the page is built again)
        Settle();
        return (new WeakReference(page), new WeakReference(SearchBox(w)));
    }

    private static TextBox SearchBox(Window w) => w.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("Search", StringComparison.Ordinal) == true);

    private static Control Show(Window w, Control page)
    {
        w.Content = page;
        Settle();
        return page;
    }

    /// <summary>Renders a frame (the drawing tree lets go of what was removed) and collects garbage.</summary>
    private static void Free(Window w)
    {
        for (var i = 0; i < 3; i++)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Settle();
#pragma warning disable S1215 // the test checks that the old page can be collected
            GC.Collect();
#pragma warning restore S1215
            GC.WaitForPendingFinalizers();
        }

        GC.KeepAlive(w);
    }
}
