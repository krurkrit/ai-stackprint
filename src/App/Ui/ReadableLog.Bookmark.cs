// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The bookmark controls at the top of a step's popup: a Bookmark toggle and a note. They are kept in state.json, on this machine only.</summary>
internal partial class ReadableLog
{
    private static Control BookmarkBar(LogEntry e, StepContext marks)
    {
        var now = marks.Get(e);
        var star = new ToggleButton
        {
            Content = "Bookmark",
            IsChecked = now?.Starred ?? false,
            Padding = new Avalonia.Thickness(Space.L, Space.XS)
        };
        var note = new TextBox
        {
            Text = now?.Note,
            PlaceholderText = "Add a note",
            MinWidth = Widths.Field
        };
        void Save() => marks.Set(e, star.IsChecked is true, note.Text);
        star.IsCheckedChanged += (_, _) => Save();
        note.LostFocus += (_, _) => Save();
        note.DetachedFromVisualTree += (_, _) => Save();   // the popup closed with the note still being typed
        var bar = new DockPanel();
        DockPanel.SetDock(star, Dock.Left);
        bar.Children.Add(star);
        bar.Children.Add(note.With(t => t.Margin = new Avalonia.Thickness(Space.M, 0, 0, 0)));
        ToolTip.SetTip(star, "Keep this step in the Bookmarks list of the file");
        return bar;
    }

    /// <summary>
    /// The bookmark button of a step in the Conversation and Compact views: a click bookmarks the step or takes the bookmark off (a note stays).
    /// It shows while <paramref name="host"/> is hovered, and always when the step has a bookmark or a note; it follows changes made elsewhere (the popup).
    /// </summary>
    internal static Button BookmarkToggle(LogEntry e, StepContext marks, Control host)
    {
        var hover = false;
        var button = IconBtn("bookmark", "Bookmark this step", () =>
        {
            var now = marks.Get(e);
            marks.Set(e, now?.Starred != true, now?.Note);
        });
        void Show()
        {
            var b = marks.Get(e);
            var (icon, key, tip) = b switch
            {
                { Starred: true } => ("bookmark_filled", Accent, b.Note ?? "Remove the bookmark"),
                { Note: { } note } => ("document_edit", Accent, note),
                _ => ("bookmark", Muted, "Bookmark this step")
            };
            button.Content = Icon(icon, IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, key);
            ToolTip.SetTip(button, tip);
            button.Opacity = hover || b != null ? 1 : 0;
        }

        host.PointerEntered += (_, _) =>
        {
            hover = true;
            Show();
        };
        host.PointerExited += (_, _) =>
        {
            hover = false;
            Show();
        };
        marks.Watch(e, Show);
        Show();
        return button;
    }
}
