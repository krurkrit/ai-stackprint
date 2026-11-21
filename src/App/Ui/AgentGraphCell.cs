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

    public AgentGraphCell(GraphRow row, int lanes, string kind, string? tip, double nodeY)
    {
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
    public static double WidthFor(int lanes) => lanes * Widths.GraphLane + Space.L;

    private static double X(int lane) => Space.M + lane * Widths.GraphLane;

    protected override Size MeasureOverride(Size availableSize) => new(WidthFor(_lanes), 0);

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    private static string AgentKey(int agent) => W.CategoryKey(Ui.Theme.Categories[(agent - 1) % Ui.Theme.Categories.Length]);

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        var y = _nodeY;
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
        var fill = owner.Agent > 0 ? Brush(AgentKey(owner.Agent)) : Brush(_kind == "user" ? W.Accent : _kind == "assistant" ? W.Text : W.Muted);
        var r = owner.Agent == 0 && _kind == "user" ? Sizes.GraphNode + 1 : _kind is "tool" or "result" ? Sizes.GraphNode - 1 : Sizes.GraphNode;   // you: larger; tool calls and results: smaller
        context.DrawEllipse(fill, new Pen(Brush(W.Surface), Stroke.Medium), new Point(X(owner.Lane), y), r, r);
    }

    private static void Curve(DrawingContext context, IPen pen, Point from, Point to)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(from, false);
            c.CubicBezierTo(new Point(from.X, (from.Y + to.Y) / 2 + (to.Y - from.Y) / 4), new Point(to.X, (from.Y + to.Y) / 2 - (to.Y - from.Y) / 4), to);
            c.EndFigure(false);
        }

        context.DrawGeometry(null, pen, g);
    }
}
