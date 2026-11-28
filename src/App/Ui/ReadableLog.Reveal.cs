// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Going to a step (a search result, bookmark or error) and tinting it, and the steps a followed file added.</summary>
internal partial class ReadableLog
{
    private StepIndex? _index;

    /// <summary>The steps by their file and line: how a search result, a bookmark or an error finds its step.</summary>
    internal StepIndex Index => _index ??= new StepIndex(Steps);

    /// <summary>
    /// Goes to the step that holds a place in a file: <paramref name="source"/> is a sub-agent's file, null the session file. A reply that spans lines holds them all; steps that
    /// start on the same line are all tinted. False when no shown step starts at or before the line.
    /// </summary>
    /// <param name="smooth">Glide to it when it is near (a bookmark clicked); search results and the rest jump there, since the steps passed on the way would be built.</param>
    public bool RevealAt(string? source, long line, double fade, bool smooth = false)
    {
        var found = Index.Find(source, line);
        if (found.Count == 0)
        {
            return false;
        }

        Reveal(found[0], fade, smooth);
        Flash(found.Skip(1), fade);
        return true;
    }

    /// <summary>Scrolls to a step and opens it if folded (search result click).</summary>
    public void Reveal(LogEntry e) => Reveal(e, 0);

    /// <param name="fade">More than 0: the step is tinted and the tint fades out over this many seconds (where a search result or bookmark is).</param>
    /// <param name="smooth">See <see cref="RevealAt"/>.</param>
    public void Reveal(LogEntry e, double fade, bool smooth = false)
    {
        var i = Steps.IndexOf(e);
        if (i >= 0 && (i < _lo || i >= _hi))
        {
            BuildAround(i);   // a search hit, bookmark or error that is not built: the steps around it are built instead
        }

        if (!_controls.TryGetValue(e, out var c))
        {
            return;
        }

        if (c is Expander x)
        {
            x.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(smooth ? () => Motion.ScrollIntoView(c) : c.BringIntoView, DispatcherPriority.Background);   // smooth: a near step glides into view, a far one is jumped to
        Flash([e], fade);
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
