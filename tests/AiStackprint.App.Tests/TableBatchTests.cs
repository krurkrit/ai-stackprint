// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class TableBatchTests
{
    private static IEnumerable<TableRow> Rows(int n, Action<int>? made = null) =>
        Enumerable.Range(0, n).Select(i => { made?.Invoke(i); return new TableRow([W.Txt($"row {i}", TextSize.Body)]); });

    private static int Shown(Window w) => Ui.Texts(w).Count(t => t.StartsWith("row ", StringComparison.Ordinal));

    [AvaloniaFact]
    public void A_long_table_builds_its_first_rows_at_once_and_the_rest_after_the_page_is_shown()
    {
        var made = 0;
        var w = new Window { Width = 600, Height = 400, Content = Table.Build([new Col("Name", "*")], Rows(120, _ => made++), "none", 40) };
        Assert.Equal(40, made);   // the rows are made as they are asked for: only the first batch so far
        w.Show();
        Assert.True(Shown(w) >= 40);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Ui.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return Shown(w) == 120; });
        Assert.Equal(120, made);
    }

    [AvaloniaFact]
    public void Without_a_batch_every_row_is_built_at_once_and_an_empty_table_says_so()
    {
        var made = 0;
        var w = new Window { Width = 600, Height = 400, Content = Table.Build([new Col("Name", "*")], Rows(120, _ => made++), "none") };
        Assert.Equal(120, made);
        w.Content = Table.Build([new Col("Name", "*")], Rows(0), "Nothing here.", 40);
        w.Show();
        Assert.Contains("Nothing here.", Ui.Texts(w));
    }

    [AvaloniaFact]
    public void A_table_that_leaves_the_screen_stops_building_rows()
    {
        var made = 0;
        var table = Table.Build([new Col("Name", "*")], Rows(400, _ => made++), "none", 40);
        var w = new Window { Width = 600, Height = 400, Content = table };
        w.Show();
        w.Content = new Border();   // the page is left before the rest is built
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(made < 400, $"{made} rows were built after the table left the screen");
    }
}
