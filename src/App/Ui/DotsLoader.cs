// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// The loading icon, big or small: a ring of dots with a bright head that runs around it and a fading tail, all in one color (the accent by default; a small
/// spinner on an accent button takes the on-accent color). Big sizes (<see cref="Sizes.Loader"/>) have 12 fine dots; small ones, beside text, 8 larger dots so they stay clear at 16 px.
/// It turns only while on screen, and carries <see cref="Busy.Class"/>.
/// </summary>
internal sealed class DotsLoader : Control
{
    private const double Box = 100;          // the drawing's own units; the control scales it to its size
    private const double BigFrom = 48;       // from this size (px) the fine ring is used
    private const double FaintestAlpha = 0.15;

    /// <param name="Count">Dots in the ring.</param>
    /// <param name="Orbit">Radius of the ring, in box units.</param>
    /// <param name="Head">Radius of the brightest dot.</param>
    /// <param name="Tail">Radius of the faintest dot.</param>
    /// <param name="Seconds">One lap.</param>
    private sealed record Look(int Count, double Orbit, double Head, double Tail, double Seconds);

    private static readonly Look _big = new(12, 38, 8, 3.5, 1.3);
    private static readonly Look _small = new(8, 34, 14, 6, 0.9);

    private readonly Look _look;
    private readonly string _key;
    private double _seconds;
    private TimeSpan? _start;
    private bool _on;

    public DotsLoader(double size, string key = W.Accent)
    {
        Width = size;
        Height = size;
        _key = key;
        _look = size >= BigFrom ? _big : _small;
        IsHitTestVisible = false;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Classes.Add(Busy.Class);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _on = true; _start = null;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _on = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Frame(TimeSpan now)
    {
        if (!_on)
        {
            return;
        }

        _start ??= now;
        _seconds = (now - _start.Value).TotalSeconds;
        InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    public override void Render(DrawingContext context)
    {
        var color = this.TryFindResource(_key, ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : Colors.SteelBlue;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Box;
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var n = _look.Count;
        var head = _seconds / _look.Seconds % 1 * n;   // where the head is, in dots (fractional: the run is smooth)
        for (var i = 0; i < n; i++)
        {
            var strength = 1 - ((head - i) % n + n) % n / n;   // 1 at the head, falling to just above 0 at the end of the tail
            var angle = (i * 360.0 / n - 90) * Math.PI / 180;
            var radius = (_look.Tail + (_look.Head - _look.Tail) * strength) * scale;
            var at = new Point(middle.X + _look.Orbit * scale * Math.Cos(angle), middle.Y + _look.Orbit * scale * Math.Sin(angle));
            context.DrawEllipse(new SolidColorBrush(color, FaintestAlpha + (1 - FaintestAlpha) * strength), null, at, radius, radius);
        }
    }
}
