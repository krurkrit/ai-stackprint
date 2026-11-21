// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A step's Copy button (shows while the pointer is over the step): the step as Markdown on the clipboard.</summary>
internal partial class ReadableLog
{
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
        if (marks is { CanMark: true } && e.Line > 0)
        {
            var mark = IconBtn(marks.Get(e)?.Starred == true ? "bookmark_filled" : "bookmark", "Bookmark this step", () => { });
            mark.Click += (_, _) =>
            {
                var now = marks.Get(e);
                marks.Set(e, now?.Starred != true, now?.Note);
                mark.Content = Icon(marks.Get(e)?.Starred == true ? "bookmark_filled" : "bookmark", IconSize.S).Themed(TextElement.ForegroundProperty, Muted);
            };
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, Space.XS, Space.XL + Space.XXL, 0);
            mark.Opacity = 0;
            holder.PointerEntered += (_, _) => mark.Opacity = 1;
            holder.PointerExited += (_, _) => mark.Opacity = marks.Get(e)?.Starred == true ? 1 : 0;
            holder.Children.Add(mark);
        }
    }
}
