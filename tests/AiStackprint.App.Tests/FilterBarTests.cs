// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class FilterBarTests
{
    private sealed record Thing(string Name, string Color, string Size);

    private static readonly List<Thing> _all = [new("a", "red", "S"), new("b", "red", "L"), new("c", "blue", "S"), new("d", "green", "L")];

    private static (FilterBar<Thing> Bar, HashSet<string> Colors, HashSet<string> Sizes) Make(Func<string>? search = null)
    {
        HashSet<string> colors = [], sizes = [];
        var bar = new FilterBar<Thing>(search ?? (() => ""), () => { }, () => { });
        bar.Add("Color", colors, t => [t.Color]);
        bar.Add("Size", sizes, t => [t.Size]);
        return (bar, colors, sizes);
    }

    [AvaloniaFact]
    public void Apply_returns_what_every_filter_lets_through()
    {
        var (bar, colors, sizes) = Make();
        colors.Add("red");
        Assert.Equal(["c", "d"], bar.Apply(_all, _all).Select(t => t.Name));
        sizes.Add("L");
        Assert.Equal(["c"], bar.Apply(_all, _all).Select(t => t.Name));
        Assert.False(bar.AtStart);
        bar.Clear();
        Assert.True(bar.AtStart);
        Assert.Equal(4, bar.Apply(_all, _all).Count);
    }

    [AvaloniaFact]
    public void A_search_that_hides_a_value_keeps_it_off_and_a_value_no_item_has_any_more_is_dropped()
    {
        var (bar, colors, _) = Make();
        colors.Add("green");
        bar.Apply([.. _all.Where(t => t.Color == "red")], _all);   // the search shows only red ones
        Assert.Contains("green", colors);   // still off: it applies again when the search is cleared
        bar.Apply([.. _all.Where(t => t.Color != "green")], [.. _all.Where(t => t.Color != "green")]);   // no item is green any more
        Assert.Empty(colors);
    }

    [AvaloniaFact]
    public void Only_leaves_one_value_on_at_the_next_Apply()
    {
        var (bar, colors, _) = Make();
        bar.Only("Color", "blue");
        Assert.Equal(["c"], bar.Apply(_all, _all).Select(t => t.Name));
        Assert.Equal(["green", "red"], colors.Order());
        Assert.Equal(["c"], bar.Apply(_all, _all).Select(t => t.Name));   // once: the filter stays as set
    }

    [AvaloniaFact]
    public void The_menu_counts_follow_the_other_filters_and_the_search()
    {
        var (bar, _, sizes) = Make();
        sizes.Add("L");
        var w = Ui.Show(bar.Build(new TextBox()));
        bar.Apply(_all, _all);
        var color = Ui.FilterBoxes(w, "Color").Select(b => string.Join(" ", Ui.Texts(b))).ToList();
        Assert.Equal(["blue 1", "green 0", "red 1"], color);   // counted among the small ones: green has none
        Ui.FilterMenu(w, "Color").Flyout!.Hide();
        Assert.True(Ui.FilterMenu(w, "Size").IsVisible);
        bar.Apply([.. _all.Where(t => t.Size == "S")], _all);   // the search leaves one size: nothing to choose
        Assert.False(Ui.FilterMenu(w, "Size").IsVisible);
    }

    [AvaloniaFact]
    public void The_help_button_sits_after_the_search_and_opens_the_search_explanation()
    {
        var (bar, _, _) = Make();
        var w = Ui.Show(bar.Build(new TextBox(), SearchHelp.Button()));
        var help = w.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "How the search works");
        help.Flyout!.ShowAt(help);
        Dispatcher.UIThread.RunJobs();
        var texts = Ui.Texts(((Flyout)help.Flyout).Content as Visual ?? help);
        Assert.Contains("author:\"Lauren\"", texts);
        Assert.Contains(texts, t => t.StartsWith("Every word you type must match", StringComparison.Ordinal));
        help.Flyout.Hide();
    }

    [AvaloniaFact]
    public void The_search_has_its_own_row_in_a_narrow_window_and_shares_the_row_in_a_wide_one()
    {
        foreach (var (width, oneRow) in new[] { (1400.0, true), (320.0, false) })
        {
            var (bar, _, _) = Make();
            var search = new TextBox();
            var w = Ui.Show(bar.Build(search));
            w.Width = width;
            bar.Apply(_all, _all);
            Dispatcher.UIThread.RunJobs();
            var menu = Ui.FilterMenu(w, "Color");
            var top = menu.TranslatePoint(default, search)!.Value.Y;
            Assert.True(oneRow ? Math.Abs(top) < search.Bounds.Height : top >= search.Bounds.Height, $"{width}: menu at {top}");
            w.Close();
        }
    }
}
