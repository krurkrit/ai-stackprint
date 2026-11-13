// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Donut chart: one slice per value, colored by resource key, with a big number in the middle. Zero values get no slice.</summary>
internal static class PieChart
{
    private const double Size = 240, Outer = 112, Inner = 70;

    public static Control Build(IReadOnlyList<(double Value, string Key)> slices, string center, string caption)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        double c = Size / 2, total = slices.Sum(s => s.Value);
        if (total <= 0)
        {
            canvas.Children.Add(Slice(c, 0, 359.99, BorderKey));
        }
        else
        {
            double at = 0;
            foreach (var (value, key) in slices.Where(s => s.Value > 0))
            {
                var sweep = value / total * 360;
                canvas.Children.Add(Slice(c, at, Math.Min(at + sweep, at + 359.99), key));
                at += sweep;
            }
        }
        var mid = new StackPanel { Width = Inner * 2 - 16, Children = { Title(center, 30).With(t => t.TextAlignment = TextAlignment.Center), Txt(caption, 12, false, Muted).With(t => t.TextAlignment = TextAlignment.Center) } };
        Canvas.SetLeft(mid, c - mid.Width / 2); Canvas.SetTop(mid, c - 24);
        canvas.Children.Add(mid);
        return canvas;
    }

    /// <summary>Ring segment from <paramref name="a0"/> to <paramref name="a1"/> degrees (0 = top, clockwise); a card-colored edge leaves a gap between slices.</summary>
    private static Control Slice(double c, double a0, double a1, string key)
    {
        Point P(double r, double deg) { var a = (deg - 90) * Math.PI / 180; return new Point(c + r * Math.Cos(a), c + r * Math.Sin(a)); }
        var large = a1 - a0 > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(Outer, a0), true);
            ctx.ArcTo(P(Outer, a1), new Size(Outer, Outer), 0, large, SweepDirection.Clockwise);
            ctx.LineTo(P(Inner, a1));
            ctx.ArcTo(P(Inner, a0), new Size(Inner, Inner), 0, large, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return new Avalonia.Controls.Shapes.Path { Data = g, StrokeThickness = 2, StrokeJoin = PenLineJoin.Round }
            .Themed(Shape.FillProperty, key).Themed(Shape.StrokeProperty, Surface);
    }
}
