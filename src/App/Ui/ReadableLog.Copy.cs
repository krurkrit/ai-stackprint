// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A step's Copy button (shows while the pointer is over the step): the step as Markdown on the clipboard.</summary>
internal partial class ReadableLog
{
    /// <summary>The width the Copy and Bookmark buttons take at a step's top right (two icon buttons and their margins).</summary>
    private const double StepActions = Space.XS + (2 * (IconSize.S + (2 * Space.M))) + Space.XS;

    private static void AddCopy(Panel holder, LogEntry e, StepContext? marks)
    {
        var copy = IconBtn("copy", "Copy this step as Markdown", () =>
        {
            if (TopLevel.GetTopLevel(holder) is { } top)
            {
                Toast.Copy(top, SessionExport.Markdown(e), "Step as Markdown");
            }
        });
        copy.HorizontalAlignment = HorizontalAlignment.Right;
        copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(0, Space.XS, Space.XS, 0);
        copy.Opacity = 0;
        holder.PointerEntered += (_, _) => copy.Opacity = 1;
        holder.PointerExited += (_, _) => copy.Opacity = 0;
        holder.Children.Add(copy);
        if (marks?.CanMarkStep(e) == true)
        {
            var mark = BookmarkToggle(e, marks, holder);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, Space.XS, Space.XL + Space.XXL, 0);
            holder.Children.Add(mark);
        }
    }
}
