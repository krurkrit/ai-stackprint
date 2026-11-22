// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Asks for the part of the log before (<see cref="Earlier"/>) or after (<see cref="Later"/>) the one shown; null where there is none.</summary>
/// <param name="File">The file shown (its steps have no <see cref="LogEntry.Source"/>): named where the parts of a text log meet.</param>
internal sealed record Paging(Action? Earlier, Action? Later, string? File = null, string? EarlierLabel = null, string? LaterLabel = null);

/// <summary>A step to keep in place when a view replaces another: its file, line and which of the steps on that line, and its height in the view.</summary>
internal sealed record Anchor(string? Source, long Line, int Nth, double Y)
{
    public int IndexIn(List<LogEntry> steps)
    {
        var n = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Line == Line && string.Equals(steps[i].Source, Source, StringComparison.OrdinalIgnoreCase) && n++ == Nth)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// Only the steps near the view are built: scrolling builds the next ones above or below and drops those far away (<see cref="MaxBuilt"/>), so a long part of a log
/// costs no more controls than a short one. Near the first or last quarter of the steps the view asks for the part before or after it (<see cref="Paging"/>).
/// </summary>
internal partial class ReadableLog
{
    private readonly Paging? _paging;
    private bool _asked;                      // the next part has been asked for: once per view
    private Button? _pageUp, _pageDown;       // at the top and bottom of the part: read the part before or after it
    private readonly Dictionary<LogEntry, Control> _slots = [];   // what each built step put in the list (its holder, with a chip or divider above)

    /// <summary>Steps built at most: the ones far from the view are dropped.</summary>
    private int MaxBuilt => More * 5;

    /// <summary>Step <paramref name="i"/> in the list: its holder, with a Sub-agent chip or a file divider above it where they change.</summary>
    private Control Built(int i)
    {
        var e = Steps[i];
        var holder = Holder(e, i);
        Control? above = null;
        if (e.SubAgent && (i == 0 || !Steps[i - 1].SubAgent) && _graph == null)   // with the graph, the lines run on and the agent is named on hover
        {
            above = Chip("Sub-agent", Accent).With(c => c.Margin = new Thickness(Space.XXXL, Space.S, 0, 0));
        }
        else if (_lines && i > 0 && !string.Equals(Steps[i - 1].Source, e.Source, StringComparison.OrdinalIgnoreCase))
        {
            above = Txt($"{FileName(Steps[i - 1].Source)} ends · {FileName(e.Source)} starts", TextSize.Caption, false, Muted).With(t =>
            {
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, Space.M);
                t.IsHitTestVisible = false;
            });
        }

        Control slot = above == null ? holder : new StackPanel { Children = { above, holder } };
        _slots[e] = slot;
        return slot;
    }

    private string FileName(string? source) => Path.GetFileName(source ?? _paging?.File ?? "");

    /// <summary>Builds up to <paramref name="count"/> more steps after the ones built; the steps far above the view are dropped, and the view stays where it is.</summary>
    private void BuildLater(int count)
    {
        var from = _hi;
        var to = Math.Min(Steps.Count, from + count);
        RemoveEdges();
        for (var i = from; i < to; i++)
        {
            _list.Children.Add(Built(i));
        }

        _hi = to;
        AddEdges();
        if (_hi - _lo > MaxBuilt)
        {
            DropTop(_hi - MaxBuilt);
        }
    }

    /// <summary>Drops the built steps before index <paramref name="to"/>; the view does not move (the offset shrinks by what went).</summary>
    private void DropTop(int to) => KeepingPlace(() =>
    {
        RemoveEdges();
        Unbuild(_lo, to);
        _lo = to;
        AddEdges();
    });

    /// <summary>Takes steps <paramref name="from"/> to <paramref name="to"/> out of the list and forgets their controls.</summary>
    private void Unbuild(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            var e = Steps[i];
            if (_slots.Remove(e, out var slot))
            {
                _list.Children.Remove(slot);
            }

            _holders.Remove(e);
            _controls.Remove(e);
            _showStep.Remove(e);
        }

        if (to >= _hi)
        {
            _hi = Math.Min(_hi, from);
        }
    }

    /// <summary>Builds the steps around step <paramref name="i"/> instead of the ones built (going to a step far from the view).</summary>
    private void BuildAround(int i)
    {
        RemoveEdges();
        Unbuild(_lo, _hi);
        _lo = _hi = i;
        BuildLater(More);
        BuildEarlier(More);
    }

    private void RemoveEdges()
    {
        foreach (var b in new[] { _earlier, _pageUp, _pageDown })
        {
            if (b != null)
            {
                _list.Children.Remove(b);
            }
        }

        _earlier = _pageUp = _pageDown = null;
    }

    /// <summary>Above the steps: "Show earlier steps" while some of this part are not built, else the button for the part before; below: the button for the part after.</summary>
    private void AddEdges()
    {
        if (_lo > 0)
        {
            _earlier = Btn($"Show {Math.Min(More, _lo)} earlier steps ({_lo} not shown)", () => BuildEarlier(More));
            _earlier.HorizontalAlignment = HorizontalAlignment.Center;
            _list.Children.Insert(_headerCount, _earlier);
        }
        else if (_paging?.Earlier is { } earlier && Steps.Count > 0)
        {
            _pageUp = PageButton(_paging.EarlierLabel ?? "Show the earlier part of the file", "chevron_up", earlier);
            _list.Children.Insert(_headerCount, _pageUp);
        }

        if (_hi == Steps.Count && _paging?.Later is { } later && Steps.Count > 0)
        {
            _pageDown = PageButton(_paging.LaterLabel ?? "Show the later part of the file", "chevron_down", later);
            _list.Children.Add(_pageDown);
        }
    }

    private Button PageButton(string text, string icon, Action go) => Btn(text, () => Ask(go), false, icon).With(b =>
    {
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(0, Space.M);
    });

    private void Ask(Action go)
    {
        if (_asked)
        {
            return;
        }

        _asked = true;
        foreach (var b in new[] { _pageUp, _pageDown })
        {
            if (b != null)
            {
                b.IsEnabled = false;
                b.Content = Row(Busy.Spinner(), L("Reading…"));
            }
        }

        go();
    }
}
