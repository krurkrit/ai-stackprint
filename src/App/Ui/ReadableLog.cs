// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    public Control Root => _scroll;
    public List<LogEntry> Steps { get; }

    /// <summary>The whole parsed session (Steps keeps only the last ones): what Export writes.</summary>
    public Session Session { get; }

    /// <summary>Steps built when a file opens; earlier ones are built when asked for ("Show earlier steps", a search hit), so a long session opens fast.</summary>
    internal const int FirstSteps = 60;
    private const int MoreSteps = 100;

    private readonly StackPanel _list = new()
    {
        Spacing = Space.M,
        Margin = new Thickness(Space.XL)
    };
    private int _built;           // steps built, counted from the end of Steps
    private readonly bool _compact;
    private readonly BookmarkSource? _marks;
    private readonly SessionGraph? _graph;   // sub-agent branches beside the steps (null when the session has none)
    private readonly int _headerCount;   // controls above the first step (summary, graph, notes)
    private Button? _earlier;

    /// <summary>A conversation as cards.</summary>
    public ReadableLog(Session s, bool toEnd) : this(s, toEnd, false)
    {
    }

    /// <param name="compact">One line per step (a click opens its card in a popup) instead of cards.</param>
    public ReadableLog(Session s, bool toEnd, bool compact) : this(s, toEnd, compact, null)
    {
    }

    /// <param name="marks">The file's bookmarks (stars and notes on steps); null: none (a file shown cut to its end has no reliable lines).</param>
    internal ReadableLog(Session s, bool toEnd, bool compact, BookmarkSource? marks)
    {
        Session = s;
        _marks = marks;
        _compact = compact;
        var lines = IsLines(s);
        _lines = lines;
        var cap = lines ? MaxLines : MaxSteps;
        Steps = [.. s.Entries.TakeLast(cap)];
        _graph = lines ? null : SessionGraph.Layout(Steps);
        if (lines || compact || _graph != null)
        {
            _list.Spacing = 0;   // the rows carry their own spacing (a graph's lines run unbroken from row to row)
        }

        _list.Children.Add(lines ? LinesSummary(s) : Summary(s));
        if (AgentTimelines.Build(s.Entries) is { } timeline)
        {
            _list.Children.Add(AgentGraph.Build(timeline));
        }

        if (_graph != null)
        {
            _list.Margin = new Thickness(Space.XL, Space.XL, 0, Space.XL);   // the graph column runs to the right edge
            _list.Children.Add(AgentLegend(_graph));
        }

        if (s.Entries.Count > cap)
        {
            _list.Children.Add(Txt($"Showing the last {cap:N0} of {s.Entries.Count:N0} {(lines ? "lines" : "steps")}.", TextSize.Caption, false, Muted));
        }

        _headerCount = _list.Children.Count;
        BuildEarlier(FirstSteps);
        if (Steps.Count == 0)
        {
            _list.Children.Add(Txt("No readable steps in this file.", TextSize.Body, false, Muted));
        }

        _scroll.Content = _list;
        if (toEnd)
        {
            Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
        }
    }

    /// <summary>Builds up to <paramref name="count"/> more steps before the ones already shown, and keeps a "Show earlier steps" button above them while some are left.</summary>
    private void BuildEarlier(int count)
    {
        var to = Steps.Count - _built;
        var from = Math.Max(0, to - count);
        if (_earlier != null)
        {
            _list.Children.Remove(_earlier);
            _earlier = null;
        }

        var at = _headerCount;
        bool? wasSub = from > 0 ? Steps[from - 1].SubAgent : null;
        for (var i = from; i < to; i++)
        {
            var e = Steps[i];
            if (e.SubAgent && wasSub != true && _graph == null)   // with the graph, the lines run on and the agent is named on hover
            {
                _list.Children.Insert(at++, Chip("Sub-agent", Accent).With(c => c.Margin = new Thickness(Space.XXXL, Space.S, 0, 0)));
            }

            wasSub = e.SubAgent;
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
            _list.Children.Insert(at++, holder);
        }

        _built += to - from;
        if (from > 0)
        {
            _earlier = Btn($"Show {Math.Min(MoreSteps, from)} earlier steps ({from} not shown)", () => BuildEarlier(MoreSteps));
            _earlier.HorizontalAlignment = HorizontalAlignment.Center;
            _list.Children.Insert(_headerCount, _earlier);
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
        foreach (var e in Steps)
        {
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

    /// <summary>Scrolls to a step and opens it if folded (search result click).</summary>
    public void Reveal(LogEntry e)
    {
        var i = Steps.IndexOf(e);
        if (i >= 0 && i < Steps.Count - _built)
        {
            BuildEarlier(Steps.Count - _built - i);   // a search hit above what is built: build down to it
        }

        if (!_controls.TryGetValue(e, out var c))
        {
            return;
        }

        if (c is Expander x)
        {
            x.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(c.BringIntoView, DispatcherPriority.Background);
    }

    /// <summary>Steps after <paramref name="previousLast"/> (the last step of the earlier read); none when it is no longer in view.</summary>
    public List<LogEntry> After(LogEntry? previousLast)
    {
        if (previousLast == null)
        {
            return [];
        }

        var i = Steps.LastIndexOf(previousLast);
        return i < 0 ? [] : [.. Steps.Skip(i + 1)];
    }

    /// <summary>Tints the given steps and fades the tint out over <paramref name="seconds"/>.</summary>
    public void Flash(IEnumerable<LogEntry> steps, double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        foreach (var e in steps)
        {
            if (!_holders.TryGetValue(e, out var holder) || holder.Children.FirstOrDefault(c => c.IsHitTestVisible) is not { } step)
            {
                continue;
            }

            var margin = step.Margin;   // the step's own indent (20 px sides, sub-agent indent), so the highlight lines up with it
            // Fill behind the step (text keeps its contrast) and a ring on top (visible on opaque bubbles too)
            var fill = new Border
            {
                CornerRadius = new CornerRadius(Radius.L),
                Margin = margin,
                IsHitTestVisible = false
            }.Themed(Border.BackgroundProperty, HighlightKey);
            var ring = new Border
            {
                CornerRadius = new CornerRadius(Radius.L),
                Margin = margin,
                BorderThickness = new Thickness(Stroke.Medium),
                IsHitTestVisible = false
            }.Themed(Border.BorderBrushProperty, HighlightRingKey);
            holder.Children.Insert(0, fill);
            holder.Children.Add(ring);
            foreach (var b in new[] { fill, ring })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    b.Transitions = [new Avalonia.Animation.DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = TimeSpan.FromSeconds(seconds),
                        Easing = new Avalonia.Animation.Easings.CubicEaseIn()
                    }];
                    b.Opacity = 0;
                }, DispatcherPriority.Background);
            }

            DispatcherTimer.RunOnce(() =>
            {
                holder.Children.Remove(fill);
                holder.Children.Remove(ring);
            }, TimeSpan.FromSeconds(seconds + 0.5));
        }
    }
}
