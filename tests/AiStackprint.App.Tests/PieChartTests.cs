// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;

using Xunit;

using Path = Avalonia.Controls.Shapes.Path;

namespace AiStackprint.App.Tests;

public class PieChartTests
{
    private static readonly (double, string, string)[] _values = [(3, W.Accent, "A: 3"), (1, W.Muted, "B: 1")];

    [AvaloniaFact]
    public void Unfolds_and_counts_up_the_first_time_then_stays_still_for_the_same_values()
    {
        var id = nameof(Unfolds_and_counts_up_the_first_time_then_stays_still_for_the_same_values);
        var first = new PieChart(id, _values, 4, "installed");
        Assert.Contains("0", Ui.Texts(first.View));   // starts from nothing
        Assert.All(Slices(first), p => Assert.Null(p.Data));

        Ui.Show(first.View);
        Ui.WaitUntil(() => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return Ui.Texts(first.View).Contains("4"); });
        Assert.All(Slices(first), p => Assert.NotNull(p.Data));

        var again = new PieChart(id, _values, 4, "installed");   // a refresh with the same numbers
        Assert.Contains("4", Ui.Texts(again.View));
        Assert.All(Slices(again), p => Assert.NotNull(p.Data));
    }

    [AvaloniaFact]
    public void Highlight_moves_only_that_slice_out_and_back()
    {
        var chart = new PieChart(nameof(Highlight_moves_only_that_slice_out_and_back), _values, 4, "installed");
        Ui.Show(chart.View);
        chart.Highlight(W.Accent);
        Ui.WaitUntil(() => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return Moved(chart).SequenceEqual([true, false]); });   // eases out
        chart.Highlight(null);
        Ui.WaitUntil(() => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return Moved(chart).SequenceEqual([false, false]); });
    }

    private static IEnumerable<bool> Moved(PieChart c) =>
        Slices(c).Select(p => Assert.IsType<TranslateTransform>(p.RenderTransform)).Select(t => Math.Abs(t.X) + Math.Abs(t.Y) > 0.01);

    private static List<Path> Slices(PieChart c) => [.. ((Canvas)c.View).Children.OfType<Path>()];
}
