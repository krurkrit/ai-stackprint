// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>The gear's pop-up: the widget's settings (<see cref="TraySettings.Widget"/>), the same as in Settings › Usage limits.</summary>
internal sealed partial class UsageWidget
{
    private void OpenSettings()
    {
        if (_gear == null)
        {
            return;
        }

        var body = TraySettings.Widget(_h.S, _h.Limits, _h.Apply);
        body.Width = Widths.Widget - 2 * Space.XXL;
        var panel = new InfoPanel("Widget settings") { Children = { body } };
        InfoFlyout.Make(panel, PlacementMode.BottomEdgeAlignedRight).ShowAt(_gear);
    }
}
