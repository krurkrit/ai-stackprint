// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>A followed session: the steps written after it was shown are added at the end.</summary>
internal partial class ReadableLog
{
    /// <summary>
    /// The file grew (Follow): adds its new steps at the end, without building the others again, and updates the summary. False when the view must be built
    /// afresh instead: the graph (sub-agents) lays out every step, and a view that has grown to twice the steps it opens with is cut back.
    /// </summary>
    public bool Append(Session s, IReadOnlyList<LogEntry> added)
    {
        if (_lines || _graph != null || Steps.Count == 0 || added.Any(e => e.SubAgent) || (_paging == null && Steps.Count + added.Count > 2 * MaxSteps))
        {
            return false;
        }

        Session = s;
        _list.Children[0] = Summary(s, _nav);
        _shownNote?.Text = $"Showing the last {Steps.Count + added.Count:N0} of {s.Entries.Count:N0} steps.";
        var built = _hi == Steps.Count;   // the view's end is built: the new steps go under it (otherwise scrolling down builds them)
        RemoveEdges();
        foreach (var e in added)
        {
            Steps.Add(e);
            if (built)
            {
                _list.Children.Add(Built(Steps.Count - 1));
                _hi++;
            }
        }

        AddEdges();
        if (_hi - _lo > MaxBuilt)
        {
            DropTop(_hi - MaxBuilt);
        }

        _index = null;   // built again when a result or bookmark looks for a step
        return true;
    }

    /// <summary>Scrolls to the last step once the view has laid out.</summary>
    public void ScrollToEnd() => Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
}
