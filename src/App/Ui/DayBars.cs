// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Per-day stacked bars (the daily chart of the Usage card and the Usage page): one column per day, parts stacked bottom up in the order of <c>keys</c>, a tooltip over the whole column.</summary>
internal static class DayBars
{
    private const double ColumnWidth = 28;   // widest column

    /// <param name="bars">One entry per day, in order: the parts (same order and count as <paramref name="keys"/>) and the tooltip text.</param>
    /// <param name="keys">Theme color key of each part.</param>
    public static Control Build(List<(DateOnly Day, double[] Parts, string Tip)> bars, string[] keys)
    {
        var max = bars.Max(v => v.Parts.Sum());
        const double barHeight = 140;
        var grid = new Grid { ColumnSpacing = Space.XS, Height = barHeight + 22, Margin = new Thickness(0, Space.S, 0, 0) };
        var step = Math.Max(1, (int)Math.Ceiling(bars.Count / 8.0));
        for (var i = 0; i < bars.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var col = new Grid { RowDefinitions = new RowDefinitions($"{barHeight},Auto"), Background = Avalonia.Media.Brushes.Transparent };   // transparent: the tooltip works over the whole column
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = ColumnWidth };
            for (var j = bars[i].Parts.Length - 1; j >= 0; j--)
            {
                if (bars[i].Parts[j] > 0)
                {
                    stack.Children.Add(new Border { Height = Math.Max(1, barHeight * bars[i].Parts[j] / max), CornerRadius = new CornerRadius(Radius.XS) }.Themed(Border.BackgroundProperty, keys[j]));
                }
            }

            var label = Txt(i % step == 0 ? bars[i].Day.ToString("MM-dd") : "", TextSize.Tiny, false, Muted).With(t => { t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; t.HorizontalAlignment = HorizontalAlignment.Center; });
            Grid.SetRow(label, 1);
            col.Children.Add(stack); col.Children.Add(label);
            ToolTip.SetTip(col, bars[i].Tip);
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }
}
