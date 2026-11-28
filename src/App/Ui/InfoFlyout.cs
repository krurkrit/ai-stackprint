// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>The content of an ⓘ pop-up: what it explains, under the title <see cref="Heading"/>.</summary>
internal sealed class InfoPanel(string heading) : StackPanel
{
    public string Heading { get; } = heading;
}

/// <summary>
/// The pop-up of an ⓘ button (levels, search help, column headers), drawn like a dialog (<see cref="Dialogs.Card"/>): the card, a title with
/// a close button, the border and the shadow. It opens beside the button; a click outside, Esc or the close button closes it.
/// </summary>
internal static class InfoFlyout
{
    /// <summary>The class of the pop-up's presenter: no background, border or padding of its own, so only the card shows.</summary>
    public const string PresenterClass = "info-card";

    public static Flyout Make(Control content, PlacementMode placement)
    {
        var right = placement is PlacementMode.BottomEdgeAlignedRight or PlacementMode.TopEdgeAlignedRight;
        var flyout = new Flyout
        {
            Placement = placement,
            // the card has room around it for its shadow: moved by that room, the card itself sits just below the button and level with its edge
            HorizontalOffset = right ? Sizes.ShadowRoom : -Sizes.ShadowRoom,
            VerticalOffset = Space.XS - Sizes.ShadowRoom
        };
        flyout.FlyoutPresenterClasses.Add(PresenterClass);
        flyout.Content = Dialogs.Card(new StackPanel
        {
            Spacing = Space.XL,
            Children = { Dialogs.Header((content as InfoPanel)?.Heading ?? "", flyout.Hide), content }
        });
        return flyout;
    }
}
