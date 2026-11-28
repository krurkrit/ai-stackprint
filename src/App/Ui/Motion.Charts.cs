// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>Charts and numbers: bars grow and numbers count up the first time they show and when their data changes, not each time a page is opened again.</summary>
internal static partial class Motion
{
    private static readonly HashSet<string> _grown = [];
    private static readonly Dictionary<string, long> _counted = [];

    /// <summary>
    /// Bars grow from nothing (<see cref="Duration.Slow"/>, ease-out) when <paramref name="data"/> (what they show) is new: from the left, or from the bottom when
    /// <paramref name="up"/>. One transform for the whole chart: a redraw, no layout. Returns the same control.
    /// </summary>
    public static T Grow<T>(T bars, string data, bool up = false) where T : Control
    {
        Grow([bars], bars, data, up);
        return bars;
    }

    /// <summary>Like <see cref="Grow{T}(T, string, bool)"/> for several bars that grow together (the columns of a day chart, so its labels stay still), on the frames of <paramref name="host"/>.</summary>
    public static void Grow(IReadOnlyList<Control> bars, Control host, string data, bool up)
    {
        if (!_grown.Add(data) || !On || bars.Count == 0)
        {
            return;
        }

        var scales = bars.Select(b =>
        {
            b.RenderTransformOrigin = up ? RelativePoint.BottomRight : RelativePoint.TopLeft;
            var s = new ScaleTransform(up ? 1 : 0, up ? 0 : 1);
            b.RenderTransform = s;
            return s;
        }).ToList();
        Tween(host, Duration.Slow, t =>
        {
            var e = 1 - Math.Pow(1 - t, 3);
            foreach (var s in scales)
            {
                if (up) { s.ScaleY = e; } else { s.ScaleX = e; }
            }
        }, () =>
        {
            foreach (var b in bars)
            {
                b.ClearValue(Visual.RenderTransformProperty);
                b.ClearValue(Visual.RenderTransformOriginProperty);
            }
        });
    }

    /// <summary>
    /// A number tile counts from the number it showed last (0 the first time) to <paramref name="to"/> (<see cref="Duration.Slow"/>, ease-out);
    /// still when it did not change. <paramref name="key"/> names the tile.
    /// </summary>
    public static TextBlock CountUp(TextBlock text, string key, long to)
    {
        var from = _counted.TryGetValue(key, out var was) ? was : 0;
        _counted[key] = to;
        if (from == to || !On)
        {
            return text;
        }

        var shown = text.Text;
        Tween(text, Duration.Slow, t => text.Text = Math.Round(from + (to - from) * (1 - Math.Pow(1 - t, 3))).ToString("N0"), () => text.Text = shown);
        return text;
    }
}
