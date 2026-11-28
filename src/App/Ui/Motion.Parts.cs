// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>Small motions inside a page: hover colors, the selection marker gliding to the new choice, a stepped number, a group's chevron turning.</summary>
internal static partial class Motion
{
    private sealed record Mark(WeakReference<TopLevel> Top, string Value, Rect At);

    private static readonly Dictionary<string, Mark> _marks = [];
    private static readonly HashSet<string> _turned = [];
    private static readonly ConditionalWeakTable<Visual, object> _tweens = [];

    /// <summary>A row's background eases to the hover color and back (<see cref="Duration.Faster"/>); none when motion is off. A new list each call: one per row.</summary>
    public static Transitions? Hover() => On
        ? [new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(Duration.Faster) }]
        : null;

    /// <summary>
    /// The selected choice of a group (the side menu, the theme switch, a segmented control) is filled; when the choice changes, its fill glides
    /// from where the last choice of <paramref name="group"/> was to its own place (<see cref="Duration.Normal"/>, ease in-out) while its icon and text stay put.
    /// The groups are rebuilt on each change, so where the last choice was is remembered by group name. Only within one window.
    /// </summary>
    public static void Glide(Button selected, string group, string value)
    {
        void Laid(object? s, EventArgs e)
        {
            if (selected.Bounds.Width <= 0 || TopLevel.GetTopLevel(selected) is not { } top || selected.TranslatePoint(default, top) is not { } at)
            {
                return;
            }

            selected.LayoutUpdated -= Laid;
            var now = new Rect(at, selected.Bounds.Size);
            if (On && _marks.TryGetValue(group, out var last) && last.Value != value && last.Top.TryGetTarget(out var was) && was == top && last.At != now)
            {
                Move(selected, last.At, now);
            }

            _marks[group] = new Mark(new WeakReference<TopLevel>(top), value, now);
        }

        selected.LayoutUpdated += Laid;
        selected.SizeChanged += (_, _) =>
        {
            if (TopLevel.GetTopLevel(selected) is { } top && selected.TranslatePoint(default, top) is { } at && _marks.TryGetValue(group, out var m) && m.Value == value)
            {
                _marks[group] = m with { At = new Rect(at, selected.Bounds.Size) };   // the side menu opened or closed: the fill starts from its new place
            }
        };
    }

    /// <summary>Moves the button (its fill) from <paramref name="from"/> to <paramref name="to"/>, with the content held in place by the opposite transform; drawn under its neighbours meanwhile.</summary>
    private static void Move(Button b, Rect from, Rect to)
    {
        if (b.Content is not Control content || content.TranslatePoint(default, b) is not { } p)
        {
            return;
        }

        b.RenderTransformOrigin = content.RenderTransformOrigin = RelativePoint.TopLeft;
        var z = b.ZIndex;
        b.ZIndex = -1;   // the others' text shows over the fill as it passes
        Tween(b, Duration.Normal, t =>
        {
            var e = t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;   // cubic ease in-out
            double Lerp(double a, double c) => a + (c - a) * e;
            var m = Matrix.CreateScale(Lerp(from.Width / to.Width, 1), Lerp(from.Height / to.Height, 1)) * Matrix.CreateTranslation(Lerp(from.X - to.X, 0), Lerp(from.Y - to.Y, 0));
            b.RenderTransform = new MatrixTransform(m);
            content.RenderTransform = new MatrixTransform(Matrix.CreateTranslation(p.X, p.Y) * m.Invert() * Matrix.CreateTranslation(-p.X, -p.Y));
        }, () =>
        {
            b.ClearValue(Visual.RenderTransformProperty);
            content.ClearValue(Visual.RenderTransformProperty);
            b.ZIndex = z;
        });
    }

    /// <summary>A number changed by − or +: it comes in from <paramref name="dy"/> pixels (below for up, above for down) as it brightens.</summary>
    public static void Nudge(Visual v, double dy)
    {
        if (On)
        {
            Start(v, Duration.Faster, false, new(.4, dy, 1), new(1, 0, 1));
        }
    }

    /// <summary>A group of <paramref name="key"/> was opened or closed by the user: its chevron and rows built in the next moment move (<see cref="Chevron"/>).</summary>
    public static void Turn(string key)
    {
        if (On && _turned.Add(key))
        {
            DispatcherTimer.RunOnce(() => _turned.Remove(key), TimeSpan.FromMilliseconds(Duration.Gentle));   // rows built later, on scroll, stay still
        }
    }

    /// <summary>Whether the group of <paramref name="key"/> was just opened or closed.</summary>
    public static bool Turned(string key) => _turned.Contains(key);

    /// <summary>A group's chevron: down when open, turned to the right when closed; it turns there when the group was just opened or closed.</summary>
    public static Control Chevron(Control icon, string key, bool open)
    {
        var turn = new RotateTransform(open ? 0 : -90);
        icon.RenderTransform = turn;
        if (Turned(key))
        {
            var from = open ? -90 : 0;
            turn.Angle = from;
            Tween(icon, Duration.Fast, t => turn.Angle = from + ((open ? 0 : -90) - from) * (1 - Math.Pow(1 - t, 3)), () => turn.Angle = open ? 0 : -90);
        }

        return icon;
    }

    /// <summary>Calls <paramref name="frame"/> with 0 to 1 on each frame of the window for <paramref name="ms"/>, then <paramref name="done"/>; a newer tween on the same visual ends this one.</summary>
    private static void Tween(Visual v, int ms, Action<double> frame, Action done)
    {
        var me = new object();
        _tweens.AddOrUpdate(v, me);
        TimeSpan? start = null;
        frame(0);
        void Frame(TimeSpan now)
        {
            if (!_tweens.TryGetValue(v, out var current) || current != me)
            {
                return;
            }

            start ??= now;
            var t = Math.Clamp((now - start.Value).TotalMilliseconds / ms, 0, 1);
            if (t < 1 && TopLevel.GetTopLevel(v) is { } top)
            {
                frame(t);
                v.InvalidateVisual();   // a frame that changes nothing on screen draws nothing, and then no next frame would come
                top.RequestAnimationFrame(Frame);
                return;
            }

            Finish();
        }

        void Finish()
        {
            if (_tweens.TryGetValue(v, out var current) && current == me)
            {
                _tweens.Remove(v);
                done();
            }
        }

        // the end is made sure of, should frames stop (the window hidden)
        DispatcherTimer.RunOnce(Finish, TimeSpan.FromMilliseconds(ms + Duration.Faster));

        if (TopLevel.GetTopLevel(v) is { } top)
        {
            top.RequestAnimationFrame(Frame);
        }
        else
        {
            void Attached(object? s, VisualTreeAttachmentEventArgs e)
            {
                v.AttachedToVisualTree -= Attached;
                TopLevel.GetTopLevel(v)?.RequestAnimationFrame(Frame);
            }

            v.AttachedToVisualTree += Attached;
        }
    }
}
