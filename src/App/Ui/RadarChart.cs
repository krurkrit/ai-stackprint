// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Radar chart for the Models comparison: one polygon per model over six axes, each axis a percent from the worst to the best of all models (<see cref="ModelCompare"/>).
/// The raw value and the percent show in a tooltip on each corner. Each model has its own chart series color (<see cref="SeriesKeys"/>).
/// </summary>
internal static class RadarChart
{
    private const double Width = 440, Height = 300, Radius = 112, LabelWidth = 120, LabelGap = 8, Dot = 5, FillOpacity = 0.14;
    private const int Rings = 4;
    public static readonly string[] SeriesKeys = [ChartIn, ChartOut, ChartWrite, ChartRead];

    /// <param name="axes">The axes: their labels.</param>
    /// <param name="colors">The resource key of each model's color, parallel to <paramref name="scores"/>.</param>
    /// <param name="rawText">Formats the raw value of an axis (axis index, value) for the tooltip.</param>
    public static Control Build(IReadOnlyList<CompareAxis> axes, IReadOnlyList<ModelScore> scores, IReadOnlyList<string> colors, Func<int, double?, string> rawText)
    {
        var canvas = new Canvas { Width = Width, Height = Height };
        var n = axes.Count;
        Point At(int axis, double percent) => new(Width / 2 + Radius * percent / 100 * Math.Cos(Angle(axis, n)), Height / 2 + Radius * percent / 100 * Math.Sin(Angle(axis, n)));

        for (var r = 1; r <= Rings; r++)
        {
            canvas.Children.Add(new Polygon { Points = [.. Enumerable.Range(0, n).Select(a => At(a, 100.0 * r / Rings))], StrokeThickness = Stroke.Thin }
                .Themed(Shape.StrokeProperty, BorderKey));
        }

        for (var a = 0; a < n; a++)
        {
            canvas.Children.Add(new Line { StartPoint = new Point(Width / 2, Height / 2), EndPoint = At(a, 100), StrokeThickness = Stroke.Thin }.Themed(Shape.StrokeProperty, BorderKey));
            canvas.Children.Add(AxisLabel(axes[a].Label, At(a, 100), Math.Cos(Angle(a, n))));
        }

        for (var i = 0; i < scores.Count; i++)
        {
            var key = colors[i];
            var pts = Enumerable.Range(0, n).Select(a => At(a, scores[i].Percent[a])).ToList();
            canvas.Children.Add(new Polygon { Points = pts, Opacity = FillOpacity, IsHitTestVisible = false }.Themed(Shape.FillProperty, key));
            canvas.Children.Add(new Polygon { Points = pts, StrokeThickness = Stroke.Medium, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false }.Themed(Shape.StrokeProperty, key));
        }

        // Corners last, so they sit on top and take the pointer
        for (var i = 0; i < scores.Count; i++)
        {
            var key = colors[i];
            for (var a = 0; a < n; a++)
            {
                var p = At(a, scores[i].Percent[a]);
                var raw = rawText(a, scores[i].Raw[a]);
                var dot = new Ellipse { Width = Dot * 2, Height = Dot * 2, StrokeThickness = Stroke.Medium }.Themed(Shape.FillProperty, key).Themed(Shape.StrokeProperty, Surface);
                ToolTip.SetTip(dot, $"{scores[i].Key}\n{axes[a].Label}: {raw}" + (scores[i].Raw[a] == null ? "" : $"  ({scores[i].Percent[a]:0}% on the scale of all models)"));
                Canvas.SetLeft(dot, p.X - Dot); Canvas.SetTop(dot, p.Y - Dot);
                canvas.Children.Add(dot);
            }
        }

        return canvas;
    }

    private static double Angle(int axis, int n) => (axis * 360.0 / n - 90) * Math.PI / 180;   // the first axis points up, then clockwise

    /// <summary>A label just outside the axis end: centered above or below, left-aligned on the right side, right-aligned on the left.</summary>
    private static Control AxisLabel(string text, Point end, double cos)
    {
        var t = Txt(text, TextSize.Caption, false, Muted).With(b => b.Width = LabelWidth);
        t.TextAlignment = cos > 0.3 ? TextAlignment.Left : cos < -0.3 ? TextAlignment.Right : TextAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        var left = cos > 0.3 ? end.X + LabelGap : cos < -0.3 ? end.X - LabelGap - LabelWidth : end.X - LabelWidth / 2;
        var top = end.Y + (end.Y < Height / 2 - 1 ? -2 * LabelGap - Space.XS : end.Y > Height / 2 + 1 ? LabelGap : -Space.M);
        Canvas.SetLeft(t, left); Canvas.SetTop(t, top);
        return t;
    }
}
