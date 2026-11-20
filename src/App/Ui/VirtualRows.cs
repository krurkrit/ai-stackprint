// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

/// <summary>
/// The rows of a long table, built only for the part that can be seen (and a margin beyond it): a list of 450 skills holds about 25 rows of controls, not 450.
/// The page's own scroll viewer scrolls it; the panel learns what part is visible from <c>EffectiveViewportChanged</c> (the same signal Avalonia's item repeater uses).
/// Rows are made when they come near and thrown away when they are far; their heights are measured once, and rows not seen yet count with the average height,
/// so the scroll bar is about right from the start and settles as rows are seen.
/// </summary>
internal sealed class VirtualRows : Panel
{
    private const double FirstHeight = 800;   // the visible height assumed before the first layout says
    private new const double Margin = 480;        // built beyond the visible part, above and below
    private const double Unknown = -1;

    private readonly int _count;
    private readonly Func<int, Control> _make;
    private readonly double[] _heights;
    private readonly Dictionary<int, Control> _live = [];
    private double _estimate = 48;   // replaced by the average of the measured rows as soon as there is one
    private double _viewTop, _viewHeight = FirstHeight;
    private int _first = -1, _last = -1;

    /// <param name="count">Number of rows.</param>
    /// <param name="make">Builds the control of row i (called each time the row comes near).</param>
    public VirtualRows(int count, Func<int, Control> make)
    {
        _count = count;
        _make = make;
        _heights = new double[count];
        Array.Fill(_heights, Unknown);
        EffectiveViewportChanged += (_, e) =>
        {
            _viewTop = e.EffectiveViewport.Top;
            _viewHeight = e.EffectiveViewport.Height > 0 ? e.EffectiveViewport.Height : FirstHeight;
            var (a, b) = Wanted();
            if (a != _first || b != _last)
            {
                InvalidateMeasure();
            }
        };
    }

    /// <summary>How many rows have controls now.</summary>
    public int Live => _live.Count;

    private double HeightOf(int i) => _heights[i] >= 0 ? _heights[i] : _estimate;

    /// <summary>The rows to have controls: those that touch the visible part plus the margin.</summary>
    private (int First, int Last) Wanted()
    {
        if (_count == 0)
        {
            return (0, -1);
        }

        double top = Math.Max(0, _viewTop - Margin), bottom = _viewTop + _viewHeight + Margin, at = 0;
        int first = -1, last = -1;
        for (var i = 0; i < _count; i++)
        {
            var h = HeightOf(i);
            if (at + h > top && first < 0)
            {
                first = i;
            }

            if (at < bottom)
            {
                last = i;
            }
            else
            {
                break;
            }

            at += h;
        }

        return (first < 0 ? _count - 1 : first, Math.Max(last, first < 0 ? _count - 1 : first));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (a, b) = Wanted();
        foreach (var gone in _live.Keys.Where(i => i < a || i > b).ToList())
        {
            Children.Remove(_live[gone]);
            _live.Remove(gone);
        }

        double at = 0, shift = 0;   // shift: how much the rows above the visible part grew or shrank when they were measured the first time
        var tops = new double[_count];
        for (var i = 0; i < _count && i <= b; i++)
        {
            tops[i] = at;
            at += HeightOf(i);
        }

        for (var i = a; i <= b; i++)
        {
            if (!_live.TryGetValue(i, out var c))
            {
                c = _make(i);
                _live[i] = c;
                Children.Add(c);
            }

            var before = HeightOf(i);
            var wasKnown = _heights[i] >= 0;
            c.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            _heights[i] = c.DesiredSize.Height;
            if (!wasKnown && tops[i] + before <= _viewTop)
            {
                shift += _heights[i] - before;   // a row above the screen turned out another height than guessed: everything below it moves
            }
        }

        if (Math.Abs(shift) > 0.5 && this.FindAncestorOfType<ScrollViewer>() is { } sv)
        {
            // Keep what the reader looks at where it is: the scroll position moves by the same amount, after this layout pass
            Dispatcher.UIThread.Post(() => sv.Offset = new Vector(sv.Offset.X, Math.Max(0, sv.Offset.Y + shift)), DispatcherPriority.Render);
        }

        _first = a;
        _last = b;
        var known = _heights.Where(h => h >= 0).ToList();
        if (known.Count > 0)
        {
            _estimate = known.Average();
        }

        var total = 0.0;
        for (var i = 0; i < _count; i++)
        {
            total += HeightOf(i);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tops = new double[_count + 1];
        for (var i = 0; i < _count; i++)
        {
            tops[i + 1] = tops[i] + HeightOf(i);
        }

        foreach (var (i, c) in _live)
        {
            c.Arrange(new Rect(0, tops[i], finalSize.Width, _heights[i]));
        }

        return finalSize;
    }
}
