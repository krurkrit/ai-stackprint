// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

/// <summary>The pop-up of an ⓘ button (levels, search help): the top bar's color as background, like the theme switch beside it.</summary>
internal static class InfoFlyout
{
    public static Flyout Make(Control content, PlacementMode placement)
    {
        var flyout = new Flyout
        {
            Content = content,
            Placement = placement
        };
        flyout.Opened += (_, _) => content.FindAncestorOfType<FlyoutPresenter>()?.Themed(TemplatedControl.BackgroundProperty, W.TopKey);
        return flyout;
    }
}
