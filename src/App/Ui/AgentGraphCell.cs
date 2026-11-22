// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// The graph beside one step of a conversation, like a row of <c>git log --graph</c>: the main line, a line for every sub-agent that is running (a dot only in the
/// lane of the agent that made this step), and the curves where an agent branches off or rejoins. It fills the whole row, so the lines are unbroken from row to row.
/// </summary>
internal sealed class AgentGraphCell : Control
{
    private readonly GraphRow _row;
    private readonly string _kind;
    private readonly int _lanes;
    private readonly double _nodeY;   // where the step's dot sits: the middle of its line
    private readonly Func<bool> _marked;   // the step is bookmarked

    public AgentGraphCell(GraphRow row, int lanes, string kind, string? tip, double nodeY, Func<bool> marked)
    {
        _marked = marked;
        _nodeY = nodeY;
        _row = row;
        _lanes = lanes;
        _kind = kind;
        IsHitTestVisible = tip != null;
        if (tip != null)
        {
            ToolTip.SetTip(this, tip);
        }
    }

    /// <summary>Width of the graph for this many lanes.</summary>
    public static double WidthFor(int lanes) => (lanes - 1) * Widths.GraphLane + 2 * Space.L;

    private static double X(int lane) => Space.L + lane * Widths.GraphLane;

    protected override Size MeasureOverride(Size availableSize) => new(WidthFor(_lanes), 0);

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    private static string AgentKey(int agent) => W.CategoryKey(Ui.Theme.Categories[(agent - 1) % Ui.Theme.Categories.Length]);

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        var y = _nodeY;
        using (context.PushOpacity(BackgroundOpacity))
        {
            context.FillRectangle(Brush(W.SideKey), new Rect(Bounds.Size));   // the side menu's background, half see-through, so the steps' row lines show under it
        }

        context.DrawLine(new Pen(Brush(W.BorderKey), Stroke.Thin), new Point(Stroke.Thin / 2, 0), new Point(Stroke.Thin / 2, h));
        var main = new Pen(Brush(W.Muted), Stroke.Medium);
        context.DrawLine(main, new Point(X(0), 0), new Point(X(0), h));
        foreach (var b in _row.Open)
        {
            context.DrawLine(new Pen(Brush(AgentKey(b.Agent)), Stroke.Medium), new Point(X(b.Lane), 0), new Point(X(b.Lane), h));
        }

        foreach (var b in _row.Forks)
        {
            Curve(context, new Pen(Brush(AgentKey(b.Agent)), Stroke.Medium), new Point(X(0), y), new Point(X(b.Lane), h));   // leaves the main line, goes down
        }

        foreach (var b in _row.Joins)
        {
            Curve(context, new Pen(Brush(AgentKey(b.Agent)), Stroke.Medium), new Point(X(b.Lane), 0), new Point(X(0), y));   // comes back to the main line
        }

        var owner = _row.Owner;
        // few colors: gray for most steps, blue for you, red for an error, a sub-agent's own color on its branch; a bookmarked step has a ring in the accent
        var fill = _kind == "error" ? Brush(W.ErrorKey) : owner.Agent > 0 ? Brush(AgentKey(owner.Agent)) : Brush(_kind == "user" ? W.Accent : W.Muted);
        var r = _kind == "error" || owner.Agent == 0 && _kind == "user" ? Sizes.GraphNode + 1 : Sizes.GraphNode;   // you and errors: one larger; every other step the same
        var pos = new Point(X(owner.Lane), y);
        if (_marked())
        {
            context.DrawEllipse(null, new Pen(Brush(W.Accent), Stroke.Medium), pos, r + Stroke.Medium * 2, r + Stroke.Medium * 2);
        }

        context.DrawEllipse(fill, new Pen(Brush(W.Surface), Stroke.Outline), pos, r, r);
    }

    /// <summary>How much of the side menu's color shows behind the graph.</summary>
    internal const double BackgroundOpacity = 0.5;

    /// <summary>An S from one point to another: it leaves straight down along the first lane and arrives straight down into the second.</summary>
    private static void Curve(DrawingContext context, IPen pen, Point from, Point to)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(from, false);
            c.CubicBezierTo(new Point(from.X, to.Y), new Point(to.X, from.Y), to);
            c.EndFigure(false);
        }

        context.DrawGeometry(null, pen, g);
    }
}
