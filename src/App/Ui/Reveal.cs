// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>
/// Components appear softly: a card or page fades in when it is shown, and the data fades in when it is ready. Each component shows the loading icon
/// (<see cref="Busy.Loading"/>) while its data is not there yet, so the page's controls are on screen at once.
/// </summary>
internal static class Reveal
{
    /// <summary>Fades <paramref name="c"/> in once it is on screen (<see cref="Duration.Fast"/>; at once with animations off). Returns the same control.</summary>
    public static T Fade<T>(T c) where T : Control
    {
        Motion.Enter(c, Duration.Fast);
        return c;
    }
}
