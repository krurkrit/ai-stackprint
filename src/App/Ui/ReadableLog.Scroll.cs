// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;

namespace AiStackprint.Ui;

/// <summary>Scrolling a part of a log: the steps near the view are built, the part before or after is asked for, and the step at the top is kept in place when a part replaces another.</summary>
internal partial class ReadableLog
{
    /// <summary>
    /// The view scrolled: near the top or bottom of what is built, the next steps are built; in the first or last quarter of the part's steps, the part before or after
    /// is asked for, so it is read while there is still a quarter left to scroll through.
    /// </summary>
    private void Scrolled(double delta)
    {
        if (_changing)
        {
            return;   // the list is being changed (steps built or dropped): its own offset changes are not scrolling
        }

        _changing = true;
        try
        {
            ScrolledBy(delta);
        }
        finally
        {
            _changing = false;
        }
    }

    private bool _changing;

    private void ScrolledBy(double delta)
    {
        if (delta < 0)
        {
            if (_scroll.Offset.Y < NearTop && _lo > 0)
            {
                BuildEarlierKeepingPlace();
            }

            if (_paging?.Earlier is { } earlier && IndexAt(0) is var top && top >= 0 && top < Steps.Count / 4)
            {
                Ask(earlier);
            }
        }
        else if (delta > 0)
        {
            if (_scroll.Extent.Height - _scroll.Offset.Y - _scroll.Viewport.Height < NearTop && _hi < Steps.Count)
            {
                BuildLater(More);
            }

            if (_paging?.Later is { } later && IndexAt(1) is var bottom && bottom >= 0 && bottom >= Steps.Count - Steps.Count / 4 - 1)
            {
                Ask(later);
            }
        }
    }

    /// <summary>The index of the step at that height of the view; the first built when the view is above it (the header in view).</summary>
    private int IndexAt(double fraction) => StepAt(fraction) is { } e ? Steps.IndexOf(e, _lo, _hi - _lo) : _hi > _lo ? _lo : -1;

    /// <summary>The step at the top of the view, for the view that replaces this one: so it shows at the same height.</summary>
    public Anchor? TopAnchor()
    {
        if (StepAtTop() is not { } e || !_holders.TryGetValue(e, out var holder) || holder.TranslatePoint(default, _list) is not { } at)
        {
            return null;
        }

        var nth = 0;
        for (var i = Steps.IndexOf(e) - 1; i >= 0 && Steps[i].Line == e.Line && Steps[i].Source == e.Source; i--)
        {
            nth++;
        }

        return new Anchor(e.Source, e.Line, nth, at.Y - _scroll.Offset.Y);
    }

    /// <summary>Once laid out, scrolls so <paramref name="e"/> is <paramref name="y"/> below the top of the view.</summary>
    private void KeepAnchor(LogEntry e, double y)
    {
        void Place(object? sender, EventArgs args)
        {
            if (!_holders.TryGetValue(e, out var holder) || holder.TranslatePoint(default, _list) is not { } at || _scroll.Extent.Height <= 0)
            {
                return;
            }

            _scroll.LayoutUpdated -= Place;
            _scroll.Offset = new Vector(0, Math.Max(0, at.Y - y));
        }

        _scroll.LayoutUpdated += Place;
    }
}
