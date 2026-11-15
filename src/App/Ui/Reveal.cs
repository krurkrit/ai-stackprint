// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// Components appear softly: a card or page fades in when it is shown, and the data fades in when it is ready. Each component shows the loading icon
/// (<see cref="Busy.Loading"/>) while its data is not there yet, so the page's controls are on screen at once.
/// </summary>
internal static class Reveal
{
    private const int FadeMs = 180;

    /// <summary>Fades <paramref name="c"/> in once it is on screen. Returns the same control.</summary>
    public static T Fade<T>(T c) where T : Control
    {
        c.Opacity = 0;
        c.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }];
        c.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => c.Opacity = 1, DispatcherPriority.Render);
        return c;
    }
}
