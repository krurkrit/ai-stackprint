// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

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
}
