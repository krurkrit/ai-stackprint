// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// Short enter and leave motions (Fluent UI 2): things appear with a fade and a small slide or zoom, eased out, and leave faster, eased in.
/// Only opacity and the render transform move, so a motion redraws but never lays out again; it runs on the window's frames and costs nothing
/// once it ends. Off with Settings › Appearance › Animations, and in tests (<see cref="Disabled"/>): then everything shows and goes at once.
/// </summary>
internal static partial class Motion
{
    private static readonly ConditionalWeakTable<Visual, Run> _running = [];
    private static readonly ConditionalWeakTable<Visual, object> _hidden = [];   // left hidden by a leave
    private static bool _installed;

    /// <summary>The Animations setting.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Set by headless tests: no motion whatever the settings say, so tests stay fast and screenshots stable.</summary>
    public static bool Disabled { get; set; }

    public static bool On => Enabled && !Disabled;

    /// <summary>Every pop-up (ⓘ cards, filter menus, drop-down lists, menus) fades in and slides <see cref="Space.XS"/> from the side it opens on. Tooltips keep their own fade.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, e) =>
        {
            if (e.NewValue is true && p.Child is { } c and not ToolTip)
            {
                var above = p.Placement is PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight;
                Enter(c, Duration.Fast, above ? Space.XS : -Space.XS);
            }
        });
    }

    /// <summary>
    /// Fades <paramref name="v"/> in over <paramref name="ms"/>, moving it from <paramref name="dy"/> pixels away and from <paramref name="scale"/> to its place
    /// (cubic ease-out). It is hidden at once, so it never flashes at full size first.
    /// </summary>
    public static void Enter(Visual v, int ms, double dy = 0, double scale = 1)
    {
        if (On)
        {
            Start(v, ms, false, new(0, dy, scale), new(1, 0, 1));
        }
        else if (Stop(v) || _hidden.TryGetValue(v, out _))
        {
            Rest(v);   // a leave had hidden it
        }
    }

    /// <summary>Fades <paramref name="v"/> out over <paramref name="ms"/>, towards <paramref name="dy"/> and <paramref name="scale"/> (cubic ease-in); it stays hidden after. At once when motion is off.</summary>
    public static Task Leave(Visual v, int ms, double dy = 0, double scale = 1) =>
        On ? Start(v, ms, true, new(1, 0, 1), new(0, dy, scale)) : Task.CompletedTask;

    private readonly record struct At(double Opacity, double Dy, double Scale);

    /// <summary>One motion: where it goes from and to, and the task that ends when it ends or another motion takes over.</summary>
    private sealed class Run(Visual v, int ms, bool leave, At from, At to)
    {
        private TimeSpan? _start;
        public TaskCompletionSource Done { get; } = new();

        public void Frame(TimeSpan now)
        {
            if (!_running.TryGetValue(v, out var current) || current != this)
            {
                return;   // stopped
            }

            _start ??= now;
            var t = Math.Clamp((now - _start.Value).TotalMilliseconds / ms, 0, 1);
            if (t < 1 && TopLevel.GetTopLevel(v) is { } top)
            {
                var e = leave ? t * t * t : 1 - Math.Pow(1 - t, 3);
                Place(v, new At(from.Opacity + (to.Opacity - from.Opacity) * e, from.Dy + (to.Dy - from.Dy) * e, from.Scale + (to.Scale - from.Scale) * e));
                v.InvalidateVisual();   // a frame that draws nothing new would get no next frame
                top.RequestAnimationFrame(Frame);
                return;
            }

            End();
        }

        /// <summary>Jumps to the end: hidden after a leave, back to rest (nothing left set) after an enter.</summary>
        public void End()
        {
            _running.Remove(v);
            if (leave)
            {
                Place(v, to);
                _hidden.AddOrUpdate(v, this);
            }
            else
            {
                Rest(v);
            }

            Done.TrySetResult();
        }
    }

    private static Task Start(Visual v, int ms, bool leave, At from, At to)
    {
        Stop(v);
        _hidden.Remove(v);
        var run = new Run(v, ms, leave, from, to);
        _running.AddOrUpdate(v, run);
        Place(v, from);
        if (TopLevel.GetTopLevel(v) is { } top)
        {
            top.RequestAnimationFrame(run.Frame);
        }
        else
        {
            v.AttachedToVisualTree += Attached;   // not on screen yet (a pop-up's content): starts with the first frame there
        }

        return run.Done.Task;

        void Attached(object? s, VisualTreeAttachmentEventArgs e)
        {
            v.AttachedToVisualTree -= Attached;
            if (TopLevel.GetTopLevel(v) is { } there)
            {
                there.RequestAnimationFrame(run.Frame);
            }
            else
            {
                run.End();
            }
        }
    }

    /// <summary>Ends the motion <paramref name="v"/> may be running where it is; its task ends too. True when one was running.</summary>
    private static bool Stop(Visual v)
    {
        if (!_running.TryGetValue(v, out var old))
        {
            return false;
        }

        _running.Remove(v);
        old.Done.TrySetResult();
        return true;
    }

    /// <summary>
    /// Sets the frame. A fade alone sets no transform: a transform change makes every list inside work out again which rows are on screen,
    /// on each frame, which on a page or a log view cost more than the whole fade.
    /// </summary>
    private static void Place(Visual v, At at)
    {
        v.Opacity = at.Opacity;
        if (Math.Abs(at.Dy) > Still || Math.Abs(at.Scale - 1) > Still || v.RenderTransform is MatrixTransform)
        {
            v.RenderTransform = new MatrixTransform(Matrix.CreateScale(at.Scale, at.Scale) * Matrix.CreateTranslation(0, at.Dy));   // about the center
        }
    }

    private const double Still = 1e-6;

    private static void Rest(Visual v)
    {
        _hidden.Remove(v);
        v.ClearValue(Visual.OpacityProperty);
        if (v.RenderTransform is MatrixTransform)
        {
            v.ClearValue(Visual.RenderTransformProperty);   // only the one a motion set
        }
    }
}
