// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A parsed session shown as a conversation. Code in replies, tool inputs and tool results is syntax highlighted.</summary>
internal partial class ReadableLog
{
    internal const int MaxSteps = 400;
    private readonly Dictionary<LogEntry, Control> _controls = [];
    private readonly Dictionary<LogEntry, Panel> _holders = [];   // each step's slot: the fading highlight is laid over it
    private readonly ScrollViewer _scroll = new();
    private readonly Grid _root = new() { ColumnDefinitions = new ColumnDefinitions("*") };
    public Control Root => _root;

    public List<LogEntry> Steps { get; }

    /// <summary>The whole parsed session (Steps keeps only the last ones): what Export writes.</summary>
    public Session Session { get; private set; }

    /// <summary>
    /// Steps built when a file opens: cards are heavy (about 8 ms each), so 20; one-line rows are light, so 60. Earlier ones are built as you scroll up
    /// (and by "Show earlier steps" or a search hit), so a long session opens fast.
    /// </summary>
    internal const int FirstCards = 20, FirstLines = 60;
    private int More => _compact || _lines ? FirstLines : FirstCards;
    private const double NearTop = 600;   // scrolling up this close to the top builds the next earlier steps

    private readonly StackPanel _list = new()
    {
        Spacing = Space.M,
        Margin = new Thickness(Space.XL)
    };
    private int _lo, _hi;         // the steps built: Steps[_lo] to Steps[_hi - 1]
    private readonly bool _compact;
    private readonly StepContext? _marks;
    private readonly SessionGraph? _graph;   // the graph column beside the steps: sub-agent branches, or just the main line (null for text logs)
    private readonly int _headerCount;   // controls above the first step (summary, graph, notes)
    private Button? _earlier;
    private readonly AgentsNav? _nav;
    private readonly TextBlock? _shownNote;   // "Showing the last 400 of …" (a part read around a place, cut to its steps)

    /// <summary>A conversation as cards.</summary>
    public ReadableLog(Session s, bool toEnd) : this(s, toEnd, false)
    {
    }

    /// <param name="compact">One line per step (a click opens its card in a popup) instead of cards.</param>
    public ReadableLog(Session s, bool toEnd, bool compact) : this(s, toEnd, compact, null)
    {
    }

    /// <param name="marks">The file's bookmarks (stars and notes on steps); null: none (a file shown cut to its end has no reliable lines).</param>
    /// <param name="paging">The part of the log before or after this one can be read: scrolling near the top or the bottom asks for it.</param>
    /// <param name="anchor">The step to show first, at the same height in the view as in the view this one replaces.</param>
    internal ReadableLog(Session s, bool toEnd, bool compact, StepContext? marks, AgentsNav? agents = null, Paging? paging = null, Anchor? anchor = null)
    {
        Session = s;
        _nav = agents;
        _marks = marks;
        _compact = compact;
        _paging = paging;
        var lines = IsLines(s);
        _lines = lines;
        var cap = paging != null ? int.MaxValue : lines ? MaxLines : MaxSteps;   // a window of the log holds all its steps: only those near the view are built
        Steps = s.Entries.Count > cap ? [.. s.Entries.TakeLast(cap)] : [.. s.Entries];
        _graph = lines ? null : SessionGraph.Layout(Steps);   // only when a sub-agent branches off: a session with just the main agent has no graph
        if (lines || compact || _graph != null)
        {
            _list.Spacing = 0;   // the rows carry their own spacing (a graph's lines run unbroken from row to row)
        }

        _list.Children.Add(lines ? LinesSummary(s) : Summary(s, agents));
        if (_graph != null)
        {
            _list.Margin = new Thickness(Space.XL, Space.XL, 0, Space.XL);   // the graph column runs to the right edge
            if (_graph.Agents.Length > 1)
            {
                _list.Children.Add(AgentLegend(_graph));
            }
        }

        if (s.Entries.Count > cap)
        {
            _shownNote = Txt($"Showing the last {cap:N0} of {s.Entries.Count:N0} {(lines ? "lines" : "steps")}.", TextSize.Caption, false, Muted);
            _list.Children.Add(_shownNote);
        }

        _headerCount = _list.Children.Count;
        var at = anchor?.IndexIn(Steps) ?? -1;
        _lo = _hi = at >= 0 ? at : Steps.Count;
        if (at >= 0)
        {
            BuildLater(More);
        }

        BuildEarlier(More);
        if (Steps.Count == 0)
        {
            _list.Children.Add(Txt("No readable steps in this file.", TextSize.Body, false, Muted));
        }

        _scroll.Content = _list;
        _root.Children.Add(_scroll);
        _scroll.ScrollChanged += (_, e) => Scrolled(e.OffsetDelta.Y);
        if (at >= 0)
        {
            KeepAnchor(Steps[at], anchor!.Y);
        }
        else if (toEnd)
        {
            Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
        }
    }

    /// <summary>Builds up to <paramref name="count"/> more steps before the ones built, and keeps a "Show earlier steps" button above them while some are left.</summary>
    private void BuildEarlier(int count)
    {
        var to = _lo;
        var from = Math.Max(0, to - count);
        RemoveEdges();
        var at = _headerCount;
        for (var i = from; i < to; i++)
        {
            _list.Children.Insert(at++, Built(i));
        }

        _lo = from;
        if (_hi - _lo > MaxBuilt)
        {
            Unbuild(_lo + MaxBuilt, _hi);   // the steps far below the view: they are built again when you scroll back down
        }

        AddEdges();
    }

    /// <summary>Step <paramref name="i"/> of <see cref="Steps"/> in its slot: the card, row or line, with its usage, sub-agent indent, graph row and copy button.</summary>
    private Panel Holder(LogEntry e, int i)
    {
        var step = _lines ? LogLine(e) : _compact ? CompactRow(e, _marks) : Step(e);   // compact: one line per step, its card opens in a popup
        _controls[e] = step;
        if (!_lines && !_compact)
        {
            if (e.Usage != null)
            {
                step = WithUsage(step, e.Usage);
            }

            if (e.SubAgent)
            {
                step = new Border
                {
                    BorderThickness = new Thickness(Space.XXS, 0, 0, 0),
                    Padding = new Thickness(Space.L, 0, 0, 0),
                    Margin = new Thickness(Space.XXXL, 0, 0, 0),
                    Child = step
                }
                    .Themed(Border.BorderBrushProperty, Accent);
            }
        }

        var holder = _graph == null ? new Panel { Children = { step } } : GraphRow(step, i, e);
        if (_lines)
        {
            holder.IsVisible = !_hidden.Contains(e.Role);
        }
        else if (!_compact)
        {
            AddCopy(holder, e, _marks);
        }

        if (_graph != null && _hiddenAgents.Contains(_graph.Rows[i].Owner.Agent) && _showStep.TryGetValue(e, out var show))
        {
            show(false);   // an agent hidden by its chip stays hidden in steps built later
        }

        _holders[e] = holder;
        return holder;
    }

    /// <summary>Builds the next earlier steps above the view without moving what is on screen (the content grows upward, so the offset grows with it).</summary>
    private void BuildEarlierKeepingPlace() => KeepingPlace(() => BuildEarlier(More));

    /// <summary>Changes the list (steps built above, dropped above or below) without moving what is on screen: a step in view stays where it was.</summary>
    private void KeepingPlace(Action change)
    {
        _scroll.UpdateLayout();
        var keep = StepAtTop() ?? (_hi > _lo ? Steps[_lo] : null);   // above the first step (the header in view): the first step built stays put
        var y = keep != null && _holders.TryGetValue(keep, out var h) ? h.TranslatePoint(default, _list)?.Y : null;
        var offset = _scroll.Offset.Y;
        change();
        _scroll.UpdateLayout();
        if (y is { } before && keep != null && _holders.TryGetValue(keep, out var held) && held.TranslatePoint(default, _list) is { } after)
        {
            _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, offset + after.Y - before));
        }
    }

    /// <summary>The step in the middle of the view (the last one when the view is at its end); null when none is built.</summary>
    public LogEntry? StepAtCenter() => StepAt(.5);

    /// <summary>The first step in view (the last one built that starts at or above the top of the view); null when none is built.</summary>
    public LogEntry? StepAtTop() => StepAt(0);

    private LogEntry? StepAt(double fraction)
    {
        var middle = _scroll.Offset.Y + _scroll.Viewport.Height * fraction;
        LogEntry? found = null;
        for (var i = _lo; i < _hi; i++)
        {
            var e = Steps[i];
            if (!_holders.TryGetValue(e, out var holder) || holder.TranslatePoint(default, _list) is not { } at)
            {
                continue;
            }

            if (at.Y > middle)
            {
                break;
            }

            found = e;
        }

        return found;
    }
}
