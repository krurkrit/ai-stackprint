// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class AppsViewTests
{
    private static (Window W, AppsView View) Open(double width)
    {
        var s = new AppState
        {
            Apps =
            [
                new AppInfo("a", "Alpha", "agent", null, true, null), new AppInfo("b", "Beta", "agent", null, true, null),
                new AppInfo("c", "Gamma", "agent", null, false, null), new AppInfo("d", "Delta", "editor", null, true, null),
            ],
            Items = [.. new[] { "Plugin", "Skill", "Command", "Agent", "Config" }.Select(k => new Item(k, k.ToLowerInvariant(), "", "Alpha", "User", "/" + k) { App = "a" })]
        };
        s.Settings.HideNotInstalled = false;
        var w = new Window
        {
            Width = width,
            Height = 800
        };
        AppsView? view = null;
        var x = new Ctx(s, w, _ => { }, () => { }) { Refresh = () => w.Content = view!.Build() };
        view = new AppsView(x);
        w.Content = view.Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, view);
    }

    [AvaloniaFact]
    public void A_group_says_how_many_apps_it_holds_not_how_many_the_tool_knows()
    {
        var (w, _) = Open(1400);
        var texts = Ui.Texts(w);
        Assert.Contains("2", texts);   // the agents installed here
        Assert.DoesNotContain(texts, t => t.Contains(" of ", StringComparison.Ordinal) && t.Contains("installed", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_count_columns_are_equally_wide_and_the_status_column_is_narrower_than_a_type_column()
    {
        var cols = new[] { Cols.Name("App"), Cols.Type("Category"), Cols.Status(), Cols.Count("Plugins"), Cols.Count("Skills"), Cols.Count("Commands"), Cols.Count("Config files") };
        var counts = cols.Where(c => c.Right).Select(c => c.Width).Distinct().ToList();
        Assert.Single(counts);
        Assert.True(double.Parse(Cols.Status().Width, System.Globalization.CultureInfo.InvariantCulture) < double.Parse(Cols.Type().Width, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void In_a_narrow_window_the_table_scrolls_sideways_and_the_last_column_can_be_reached()
    {
        var (w, _) = Open(700);
        var sv = w.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        Assert.True(sv.Extent.Width > sv.Viewport.Width, $"extent {sv.Extent.Width} viewport {sv.Viewport.Width}");
        sv.Offset = new Avalonia.Vector(sv.Extent.Width, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(sv.Offset.X > 0);
        Assert.Contains("Config files", Ui.Texts(w));

        var (wide, _) = Open(1800);
        var wideSv = wide.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        Assert.True(wideSv.Extent.Width <= wideSv.Viewport.Width + 1);   // wide enough: nothing to scroll
    }

    [AvaloniaFact]
    public void A_count_column_empty_for_every_app_is_left_out()
    {
        var (w, _) = Open(1400);
        var texts = Ui.Texts(w);
        Assert.Contains("Config files", texts);
        Assert.DoesNotContain("Servers", texts);   // no app here has a server
        Assert.DoesNotContain("Hooks", texts);
    }

    private static Button Find(Window w, string text) =>
        w.GetVisualDescendants().OfType<Button>().Where(b => b is not FilterMenu).First(b => b.Content as string == text || b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == text));   // a hidden button has no visual text

    [AvaloniaFact]
    public void The_filters_are_one_row_Group_by_toggles_Category_and_Status_and_Reset_works_while_something_is_filtered()
    {
        var (w, _) = Open(1400);
        var reset = w.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Reset the search, filters and grouping");
        Assert.False(reset.IsEnabled);
        Assert.Contains("Editors", Ui.Texts(w));   // grouped by category at first

        Ui.Click(Find(w, "Status"));   // Category and Status both on: grouped by both
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Agents · Not found", Ui.Texts(w));
        Assert.True(reset.IsEnabled);   // a grouping other than the first one can be reset too
        Ui.Click(Find(w, "Category"));
        Ui.Click(Find(w, "Status"));   // both off: a plain list
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Editors", Ui.Texts(w));
        Assert.Contains("Delta", Ui.Texts(w));

        var search = w.GetVisualDescendants().OfType<TextBox>().First();
        search.Text = "Alpha";
        Dispatcher.UIThread.RunJobs();
        Assert.True(reset.IsEnabled);
        Ui.Click(reset);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", search.Text);
        Assert.False(reset.IsEnabled);
        Assert.Contains("Delta", Ui.Texts(w));
        Assert.Contains("Editors", Ui.Texts(w));   // grouped by category again
    }

    [AvaloniaFact]
    public void The_Category_menu_unchecks_a_category_and_the_line_under_the_tool_bar_lists_what_is_on()
    {
        var (w, _) = Open(1400);
        var menu = w.GetVisualDescendants().OfType<FilterMenu>().First(m => m.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Category"));
        menu.Flyout!.ShowAt(menu);
        Dispatcher.UIThread.RunJobs();
        var boxes = ((StackPanel)((Flyout)menu.Flyout).Content!).Children.OfType<Border>().Select(b => (CheckBox)b.Child!).ToList();
        Assert.Equal(2, boxes.Count);   // Agents and Editors, each with its count
        boxes[1].IsChecked = false;     // Editors off
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Status: Installed, Not found    Category: Agents    Group by: Category", Ui.Texts(w));   // the summary under the tool bar lists what is on
        Assert.DoesNotContain("Delta", Ui.Texts(w));
        boxes[0].IsChecked = false;     // the last one on stays on
        Assert.True(boxes[0].IsChecked);
        menu.Flyout.Hide();
    }

    [AvaloniaFact]
    public void A_filter_menu_has_the_accent_border_while_its_list_is_open()
    {
        var (w, _) = Open(1400);
        var menu = w.GetVisualDescendants().OfType<FilterMenu>().First(m => m.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Category"));
        menu.TryFindResource(W.Accent, menu.ActualThemeVariant, out var accent);
        Assert.NotNull(accent);
        Assert.NotEqual(accent, menu.BorderBrush);
        menu.Flyout!.ShowAt(menu);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(accent, menu.BorderBrush);   // like an open drop-down and a focused text box
        menu.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(accent, menu.BorderBrush);
    }

    [AvaloniaFact]
    public void Filtering_out_the_only_apps_with_counts_keeps_the_same_columns()
    {
        var (w, _) = Open(1400);
        var menu = w.GetVisualDescendants().OfType<FilterMenu>().First(m => m.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Category"));
        menu.Flyout!.ShowAt(menu);
        Dispatcher.UIThread.RunJobs();
        var boxes = ((StackPanel)((Flyout)menu.Flyout).Content!).Children.OfType<Border>().Select(b => (CheckBox)b.Child!).ToList();
        boxes[0].IsChecked = false;     // Agents off: Alpha, the only app with plugins or config files, is not listed
        Dispatcher.UIThread.RunJobs();
        menu.Flyout.Hide();
        var texts = Ui.Texts(w);
        Assert.DoesNotContain("Alpha", texts);
        Assert.Contains("Plugins", texts);
        Assert.Contains("Config files", texts);
        Assert.DoesNotContain("Servers", texts);   // still left out: no app has one
    }
}
