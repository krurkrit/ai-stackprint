// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>
/// A tool bar of two parts: the first (the search) takes the room the second (filters, Group by, Reset) leaves at the right, on one row while the first keeps its
/// minimum width; a narrower window puts the first on its own row, full width, with the second under it.
/// </summary>
internal sealed class ToolbarPanel : Panel
{
    private static double Gap => Space.M;

    private static bool OneRow(double width, Control first, Control second) => width - second.DesiredSize.Width - Gap >= first.MinWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
        {
            return base.MeasureOverride(availableSize);
        }

        Control first = Children[0], second = Children[1];
        var width = availableSize.Width;
        second.Measure(new Size(width, double.PositiveInfinity));
        if (double.IsInfinity(width) || OneRow(width, first, second))
        {
            first.Measure(new Size(width - second.DesiredSize.Width - Gap, double.PositiveInfinity));
            return new Size(double.IsInfinity(availableSize.Width) ? first.DesiredSize.Width + Gap + second.DesiredSize.Width : width,
                Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
        }

        first.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, first.DesiredSize.Height + Gap + second.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
        {
            return base.ArrangeOverride(finalSize);
        }

        Control first = Children[0], second = Children[1];
        if (OneRow(finalSize.Width, first, second))
        {
            var right = second.DesiredSize.Width;
            first.Arrange(new Rect(0, 0, finalSize.Width - right - Gap, finalSize.Height));
            second.Arrange(new Rect(finalSize.Width - right, 0, right, finalSize.Height));
        }
        else
        {
            first.Arrange(new Rect(0, 0, finalSize.Width, first.DesiredSize.Height));
            second.Arrange(new Rect(0, first.DesiredSize.Height + Gap, Math.Min(second.DesiredSize.Width, finalSize.Width), second.DesiredSize.Height));   // from the left, under the search
        }

        return finalSize;
    }
}
