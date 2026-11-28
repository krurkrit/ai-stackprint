// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

internal static partial class Motion
{
    /// <summary>
    /// A row taken away by the user fades out as its height closes (<see cref="Duration.Fast"/>), so the rows below move up instead of jumping;
    /// the caller removes it when the task ends (at once with animations off). For one row: a list that loses many is simply rebuilt.
    /// </summary>
    public static Task Remove(Control row)
    {
        var done = new TaskCompletionSource();
        var height = row.Bounds.Height;
        if (!On || height <= 0 || TopLevel.GetTopLevel(row) == null)
        {
            return Task.CompletedTask;
        }

        row.ClipToBounds = true;
        row.IsHitTestVisible = false;
        Tween(row, Duration.Fast, t =>
        {
            var e = t * t * t;   // ease-in: it leaves
            row.Opacity = 1 - e;
            row.Height = height * (1 - e);
        }, () => done.TrySetResult());
        return done.Task;
    }

    /// <summary>
    /// Scrolls <paramref name="c"/> into view smoothly (<see cref="Duration.Gentle"/>, ease in-out) when it is near, within one and a half screens;
    /// a far one is jumped to, since a long smooth scroll would build every row on the way. Scrolling by the user stops the motion.
    /// </summary>
    public static void ScrollIntoView(Control c)
    {
        var sv = c.FindAncestorOfType<ScrollViewer>();
        if (sv == null || !On)
        {
            c.BringIntoView();
            return;
        }

        var before = sv.Offset;
        c.BringIntoView();   // finds where it has to go
        var after = sv.Offset;
        var dy = after.Y - before.Y;
        if (Math.Abs(dy) < 1 || Math.Abs(dy) > sv.Viewport.Height * 1.5)
        {
            return;
        }

        sv.Offset = before;
        var set = before;
        var stopped = false;
        Tween(sv, Duration.Gentle, t =>
        {
            if (stopped || Math.Abs(sv.Offset.Y - set.Y) > 1)
            {
                stopped = true;   // the user (or the view keeping its top step) moved it
                return;
            }

            var e = t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            set = new Vector(after.X, before.Y + dy * e);
            sv.Offset = set;
        }, () =>
        {
            if (!stopped)
            {
                sv.Offset = after;
            }
        });
    }
}
