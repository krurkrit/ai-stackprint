// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

using static AiStackprint.Ui.W;

using Path = Avalonia.Controls.Shapes.Path;

namespace AiStackprint.Ui;

/// <summary>
/// Donut chart: one slice per value, colored by resource key, with a big number in the middle. Zero values get no slice.
/// The first time a chart shows, its slices unfold clockwise from the top while the number counts up; when the values change
/// (a rescan), the slices move to their new sizes. The same values shown again do not animate, so a refresh stays steady.
/// A slice under the pointer, or one picked with <see cref="Highlight"/>, moves out a little.
/// </summary>
internal sealed class PieChart
{
    private const double Size = 240, Outer = 112, Inner = 70, Pop = 6;
    private static readonly TimeSpan _duration = TimeSpan.FromMilliseconds(700);
    private static readonly Dictionary<string, Shown> _shown = [];   // last values drawn, by chart id

    private readonly List<Slice> _slices = [];
    private readonly TextBlock _number;
    private readonly int _from, _to;
    private TimeSpan? _start;

    /// <summary>Slice end angles (degrees) by key, and the number in the middle.</summary>
    private sealed record Shown(Dictionary<string, (double A0, double A1)> Angles, int Total);

    private sealed record Slice(string Key, Path Path, TranslateTransform Move, double From0, double From1, double To0, double To1);

    public Control View { get; }

    /// <param name="id">Names the chart so a rebuild of the same chart animates from what was shown before.</param>
    public PieChart(string id, IReadOnlyList<(double Value, string Key, string Tip)> values, int total, string caption)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        double sum = values.Sum(s => s.Value), at = 0;
        var target = new Dictionary<string, (double, double)>();
        foreach (var (value, key, _) in values.Where(s => s.Value > 0))
        {
            var sweep = value / sum * 360;
            target[key] = (at, Math.Min(at + sweep, at + 359.99));
            at += sweep;
        }

        var old = _shown.GetValueOrDefault(id);
        _shown[id] = new Shown(target, total);
        var animate = old == null || old.Total != total || !Same(old.Angles, target);
        _from = animate ? old?.Total ?? 0 : total;
        _to = total;
        if (sum <= 0)
        {
            canvas.Children.Add(Ring(0, 359.99, BorderKey));
        }

        foreach (var (_, key, tip) in values.Where(s => s.Value > 0))
        {
            var (to0, to1) = target[key];
            // New slices grow out of their own start, so the first view unfolds clockwise from the top
            var (from0, from1) = animate ? old?.Angles.GetValueOrDefault(key, (to0, to0)) ?? (0, 0) : (to0, to1);
            var move = new TranslateTransform
            {
                Transitions =
                [
                    new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() },
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() },
                ]
            };
            var path = Ring(from0, from1, key).With(p => { p.RenderTransform = move; p.RenderTransformOrigin = RelativePoint.TopLeft; });
            ToolTip.SetTip(path, tip);
            path.PointerEntered += (_, _) => Highlight(key);
            path.PointerExited += (_, _) => Highlight(null);
            _slices.Add(new Slice(key, path, move, from0, from1, to0, to1));
            canvas.Children.Add(path);
        }

        _number = Title(_from.ToString("N0"), TextSize.Display).With(t => t.TextAlignment = TextAlignment.Center);
        var mid = new StackPanel { Width = Inner * 2 - 16, IsHitTestVisible = false, Children = { _number, Txt(caption, TextSize.Caption, false, Muted).With(t => t.TextAlignment = TextAlignment.Center) } };
        Canvas.SetLeft(mid, Size / 2 - mid.Width / 2); Canvas.SetTop(mid, Size / 2 - 24);
        canvas.Children.Add(mid);
        if (animate)
        {
            canvas.AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(canvas)?.RequestAnimationFrame(Frame);
        }

        View = canvas;
    }

    /// <summary>Moves the slice for <paramref name="key"/> out from the center; null puts every slice back.</summary>
    public void Highlight(string? key)
    {
        foreach (var s in _slices)
        {
            var mid = (s.To0 + s.To1) / 2;
            var d = s.Key == key ? Pop : 0;
            s.Move.X = d * Math.Cos(Rad(mid));
            s.Move.Y = d * Math.Sin(Rad(mid));
        }
    }

    private void Frame(TimeSpan now)
    {
        _start ??= now;
        var t = Math.Clamp((now - _start.Value) / _duration, 0, 1);
        var e = 1 - Math.Pow(1 - t, 3);   // cubic ease-out
        foreach (var s in _slices)
        {
            s.Path.Data = Geometry(s.From0 + (s.To0 - s.From0) * e, s.From1 + (s.To1 - s.From1) * e);
        }

        _number.Text = Math.Round(_from + (_to - _from) * e).ToString("N0");
        if (t < 1 && TopLevel.GetTopLevel(View) is { } top)
        {
            top.RequestAnimationFrame(Frame);
        }
    }

    private static bool Same(Dictionary<string, (double A0, double A1)> a, Dictionary<string, (double A0, double A1)> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && Math.Abs(v.A0 - kv.Value.A0) < 0.01 && Math.Abs(v.A1 - kv.Value.A1) < 0.01);

    private static double Rad(double deg) => (deg - 90) * Math.PI / 180;   // 0 degrees = top, clockwise

    /// <summary>Ring segment from <paramref name="a0"/> to <paramref name="a1"/> degrees; a card-colored edge leaves a gap between slices.</summary>
    private static Path Ring(double a0, double a1, string key) =>
        new Path { Data = Geometry(a0, a1), StrokeThickness = Stroke.Medium, StrokeJoin = PenLineJoin.Round }
            .Themed(Shape.FillProperty, key).Themed(Shape.StrokeProperty, Surface);

    private static StreamGeometry? Geometry(double a0, double a1)
    {
        if (a1 - a0 < 0.05)
        {
            return null;   // nothing to draw yet
        }

        const double c = Size / 2;
        static Point P(double r, double deg) => new(c + r * Math.Cos(Rad(deg)), c + r * Math.Sin(Rad(deg)));
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

        return g;
    }
}
