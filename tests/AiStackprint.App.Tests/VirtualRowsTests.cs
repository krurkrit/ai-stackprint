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

public sealed class VirtualRowsTests
{
    private static (Window W, ScrollViewer Sv, VirtualRows Rows) Open(int count, Func<int, double> height)
    {
        var rows = new VirtualRows(count, i => new Border
        {
            Height = height(i),
            Child = new TextBlock { Text = "row " + i }
        });
        var sv = new ScrollViewer { Content = new StackPanel { Children = { new TextBlock { Text = "above the list" }, rows } } };
        var w = new Window
        {
            Width = 600,
            Height = 500,
            Content = sv
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, sv, rows);
    }

    private static void Scroll(ScrollViewer sv, double y)
    {
        sv.Offset = new Vector(0, y);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Has(Window w, string text) => Ui.Texts(w).Contains(text);

    [AvaloniaFact]
    public void Only_the_rows_near_the_screen_have_controls_and_the_scroll_extent_covers_all_rows()
    {
        var (w, sv, rows) = Open(1000, _ => 40);
        Assert.True(rows.Live < 60, $"{rows.Live} rows built for 1000");
        Assert.True(Has(w, "row 0"));
        Assert.False(Has(w, "row 999"));
        Assert.InRange(sv.Extent.Height, 39_000, 42_000);

        Scroll(sv, 20_000);
        Assert.False(Has(w, "row 0"));
        Assert.True(Has(w, "row 500") || Has(w, "row 499") || Has(w, "row 498"), string.Join(", ", Ui.Texts(w).Take(5)));
        Assert.True(rows.Live < 60);

        Scroll(sv, 1e9);
        Assert.True(Has(w, "row 999"));
        Assert.True(rows.Live < 60);
    }

    [AvaloniaFact]
    public void Scrolling_up_through_rows_of_unlike_heights_keeps_what_is_on_screen_where_it_is()
    {
        var (w, sv, _) = Open(600, i => i < 15 ? 200 : 40);   // the first rows are tall, so the guess (the average of those first rows) is far too big for all the others
        Scroll(sv, 12_000);
        Scroll(sv, sv.Offset.Y);   // settle: the first jump corrects the guess for every row, the second keeps still
        var mark = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("row ", StringComparison.Ordinal) && t.TranslatePoint(default, w) is { Y: > 250 and < 330 });
        var text = mark.Text;
        var before = mark.TranslatePoint(default, w)!.Value.Y;

        Scroll(sv, sv.Offset.Y - 150);   // up: rows above are measured for the first time and are not the guessed height
        var again = w.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);
        var moved = again.TranslatePoint(default, w)!.Value.Y - before;
        Assert.InRange(moved, 135, 165);   // 150 px of scroll moved it about 150 px: no jump from rows that turned out another height
    }

    [AvaloniaFact]
    public void A_virtual_table_has_its_header_and_says_so_when_there_are_no_rows()
    {
        var cols = new[] { new Col("Name", "*") };
        var w = new Window
        {
            Width = 600,
            Height = 400,
            Content = new ScrollViewer { Content = Table.Virtual(cols, [], "Nothing here.") }
        };
        w.Show();
        Assert.Contains("Nothing here.", Ui.Texts(w));
        Assert.Contains("Name", Ui.Texts(w));

        var made = new List<int>();
        var rows = Enumerable.Range(0, 300).Select(i => (Func<TableRow>)(() =>
        {
            made.Add(i);
            return new TableRow([W.Txt("item " + i, TextSize.Body)]);
        })).ToList();
        w.Content = new ScrollViewer { Content = Table.Virtual(cols, rows) };
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("item 0", Ui.Texts(w));
        Assert.True(made.Count < 60, $"{made.Count} of 300 rows were made");
    }

    [AvaloniaFact]
    public void A_list_up_to_the_limit_builds_every_row_and_a_longer_one_builds_the_rows_on_screen()
    {
        var keep = Table.VirtualFrom;
        try
        {
            var cols = new[] { new Col("Name", "*") };
            static IReadOnlyList<Func<TableRow>> Rows(int n) => [.. Enumerable.Range(0, n).Select(i => (Func<TableRow>)(() => new TableRow([W.Txt("item " + i, TextSize.Body)])))];
            Table.VirtualFrom = 40;
            Assert.Empty(((Panel)Table.Virtual(cols, Rows(40))).Children.OfType<VirtualRows>());
            Assert.Single(((Panel)Table.Virtual(cols, Rows(41))).Children.OfType<VirtualRows>());
            Table.VirtualFrom = 0;   // the setting's 0: every list is virtual
            Assert.Single(((Panel)Table.Virtual(cols, Rows(3))).Children.OfType<VirtualRows>());
            Table.VirtualFrom = 1000;
            Assert.Empty(((Panel)Table.Virtual(cols, Rows(300))).Children.OfType<VirtualRows>());
        }
        finally { Table.VirtualFrom = keep; }
    }
}
