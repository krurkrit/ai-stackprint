// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ReadableLog
{
    /// <summary>A tool result as a folded card; a failed one has a red bar on its left and says so.</summary>
    private static Control ResultCard(LogEntry e)
    {
        var card = Folded((e.Error ? "Failed" : "Result") + (e.Tool != null ? $" · {e.Tool}" : ""), e.Text, e.Lang);
        return e.Error
            ? new Border
            {
                BorderThickness = new Thickness(Stroke.Medium, 0, 0, 0),
                Child = card
            }.Themed(Border.BorderBrushProperty, ErrorKey)
            : card;
    }
}
