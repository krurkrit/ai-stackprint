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
    public void Skills_count_returns_to_the_total_when_All_is_picked_again_and_follows_the_level_checks()
    {
        var (w, x) = Host(State());
        var skills = new LibraryView(x, "Skill");
        w.Content = skills.Build();
        Settle();
        Assert.Contains("3", Ui.Texts(w));   // the count beside the search: all skills shown

        Radio(w, "Agent only").IsChecked = true;
        Settle();
        Assert.Contains("1 of 3", Ui.Texts(w));

        Radio(w, "All").IsChecked = true;   // the first choice again: the bug left "1 of 3"
        Settle();
        Assert.Contains("3", Ui.Texts(w));
        Assert.DoesNotContain("1 of 3", Ui.Texts(w));

        w.GetVisualDescendants().OfType<CheckBox>().First(c => Ui.Texts(c).Contains("Project")).IsChecked = false;
        Settle();
        Assert.Contains("2 of 3", Ui.Texts(w));
        Assert.Contains("All 2", Ui.Texts(w));   // the radio counts follow the level filter
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
        Assert.Contains("1 of 3", Ui.Texts(w));
        Assert.True(Radio(w, "Agent only").IsChecked);
        w.Close();
        Settle();
    }

    /// <summary>The Skills page with a search and a filter; only weak references come back, so the test itself keeps nothing of it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Page, WeakReference Box) FirstVisit(Window w, LibraryView skills)
    {
        skills.Filter(null, "review");   // as the dashboard does (headless timers do not run, so typing would not search)
        var page = Show(w, skills.Build());
        Assert.Contains("1 of 3", Ui.Texts(w));
        Radio(w, "Agent only").IsChecked = true;
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

    private static RadioButton Radio(Window w, string label) =>
        w.GetVisualDescendants().OfType<RadioButton>().First(r => r.Content is string t && t.StartsWith(label + " ", StringComparison.Ordinal));
}
