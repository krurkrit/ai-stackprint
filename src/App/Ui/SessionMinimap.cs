// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// The whole session in a thin strip beside it: one tick per step (many steps share a pixel: the strongest wins), colored by what it is, with the part in view boxed.
/// A click or drag goes to that place. Errors are red, warnings amber, bookmarks and your prompts the accent.
/// </summary>
internal sealed class SessionMinimap : Control
{
    private const int Error = 5, Warn = 4, Mark = 3, User = 2, Assistant = 1;   // the order of the brushes in Render
    private readonly byte[] _kinds;
    private readonly Func<(int Top, int Bottom)> _view;
    private readonly Action<int> _jump;

    /// <param name="marked">Whether a step is bookmarked.</param>
    /// <param name="view">The first and last step in view (indexes into <paramref name="steps"/>).</param>
    /// <param name="jump">Goes to a step.</param>
    public SessionMinimap(IReadOnlyList<LogEntry> steps, Func<LogEntry, bool> marked, Func<(int Top, int Bottom)> view, Action<int> jump)
    {
        _kinds = [.. steps.Select(e => (byte)(e.IsError ? Error : e.Role == "warn" ? Warn : marked(e) ? Mark : e.Role == "user" ? User : e.Role == "assistant" ? Assistant : 0))];
        _view = view;
        _jump = jump;
        Cursor = W.Hand;
        ToolTip.SetTip(this, "The whole session: errors red, warnings amber, bookmarks and prompts blue. Click or drag to go there.");
        PointerPressed += (_, e) =>
        {
            e.Pointer.Capture(this);
            Go(e.GetPosition(this).Y);
        };
        PointerMoved += (_, e) =>
        {
            if (e.Pointer.Captured == this)
            {
                Go(e.GetPosition(this).Y);
            }
        };
        PointerReleased += (_, e) => e.Pointer.Capture(null);
    }

    public static double WidthOf => Widths.Minimap;

    protected override Size MeasureOverride(Size availableSize) => new(Widths.Minimap, 0);

    private void Go(double y)
    {
        if (_kinds.Length > 0 && Bounds.Height > 0)
        {
            _jump(Math.Clamp((int)(y / Bounds.Height * _kinds.Length), 0, _kinds.Length - 1));
        }
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        var rows = (int)Bounds.Height;
        if (rows <= 0 || _kinds.Length == 0)
        {
            return;
        }

        context.FillRectangle(Brush(W.SideKey), new Rect(Bounds.Size));
        var strongest = new byte[rows];
        for (var i = 0; i < _kinds.Length; i++)
        {
            var row = (int)((long)i * rows / _kinds.Length);
            strongest[row] = Math.Max(strongest[row], (byte)(_kinds[i] + 1));   // +1: a plain step still draws
        }

        var brushes = new[] { Brush(W.BorderKey), Brush(W.Muted), Brush(W.Accent), Brush(W.Accent), Brush(W.WarningKey), Brush(W.ErrorKey) };   // by kind: plain, assistant, you, bookmark, warning, error
        for (var y = 0; y < rows; y++)
        {
            if (strongest[y] == 0)
            {
                continue;
            }

            var kind = strongest[y] - 1;
            var wide = kind >= Mark;   // the notable ones reach further across
            context.FillRectangle(brushes[kind], new Rect(wide ? Space.XS : Space.S, y, Bounds.Width - (wide ? Space.XS : Space.S) * 2, Stroke.Medium));
        }

        var (top, bottom) = _view();
        var y0 = top / (double)_kinds.Length * rows;
        var y1 = Math.Max(y0 + Stroke.Medium * 2, (bottom + 1) / (double)_kinds.Length * rows);
        context.DrawRectangle(Brush(W.Subtle), new Pen(Brush(W.Accent), Stroke.Thin), new Rect(0, y0, Bounds.Width, y1 - y0));
    }
}
