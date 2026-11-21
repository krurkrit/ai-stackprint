// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>The minimap beside a long session or log: its ticks, the box for what is in view, and going to a place when it is clicked.</summary>
internal partial class ReadableLog
{
    private readonly Dictionary<LogEntry, int> _index = new(ReferenceEqualityComparer.Instance);   // a step's place in Steps (steps are records that can be equal: by reference)

    private void AddMinimap()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            _index[Steps[i]] = i;
        }

        _map = new SessionMinimap(Steps, e => _marks?.Get(e) != null, VisibleRange, i => Reveal(Steps[i]));
        Grid.SetColumn(_map, 1);
        _root.Children.Add(_map);
        _scroll.ScrollChanged += (_, _) => _map.InvalidateVisual();
    }

    /// <summary>The first and last step in view, as places in <see cref="Steps"/>.</summary>
    private (int Top, int Bottom) VisibleRange()
    {
        var top = StepAtTop() is { } a && _index.TryGetValue(a, out var i) ? i : 0;
        var bottom = StepAt(1) is { } b && _index.TryGetValue(b, out var j) ? j : Steps.Count - 1;
        return (top, Math.Max(top, bottom));
    }
}
