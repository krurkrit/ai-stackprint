// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The Bookmarks button: the steps of this file you starred or wrote a note on; a click goes to the step.</summary>
internal partial class LogViewer
{
    private BookmarkSource? _marks;   // null while the file is shown cut to its end: a step's line is then not the file's line

    private void ReadMarks() => _marks = _linesOk && _file != null ? new BookmarkSource(_x.S.State, _file.Path) : null;

    /// <summary>The toolbar button; null when no session is shown as steps.</summary>
    private Button? BookmarksButton()
    {
        if (!ShowReadable || IsLines)
        {
            return null;
        }

        var button = IconBtn("bookmark", "Bookmarks and notes of this file", () => { });
        button.Click += (_, _) => ShowBookmarks(button);
        return button;
    }

    private void ShowBookmarks(Control anchor)
    {
        var list = new StackPanel { Spacing = Space.XXS, Margin = new Thickness(Space.M), Width = Widths.Popup };
        var marks = _marks?.All() ?? [];
        var flyout = new Flyout { Content = new ScrollViewer { MaxHeight = Heights.Viewer, Content = list } };
        if (_marks == null)
        {
            list.Children.Add(Txt("This file is shown cut to its end, so its steps cannot be kept by line. Open a shorter file.", TextSize.Small, false, Muted));
        }
        else if (marks.Count == 0)
        {
            list.Children.Add(Txt("No bookmarks yet. Click a line in Compact (or hover a card), then press Bookmark or add a note.", TextSize.Small, false, Muted));
        }

        foreach (var b in marks)
        {
            var row = new StackPanel
            {
                Children = { Txt($"Line {b.Line:N0} · {b.Preview}", TextSize.Small).With(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis) }
            };
            if (b.Note != null)
            {
                row.Children.Add(Txt(b.Note, TextSize.Caption, false, Muted).With(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap));
            }

            var border = new Border
            {
                Child = Row(Icon(b.Starred ? "bookmark_filled" : "document_edit", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Accent), row),
                Padding = new Thickness(Space.S),
                CornerRadius = new CornerRadius(Radius.S),
                Cursor = Hand,
                Background = Avalonia.Media.Brushes.Transparent
            };
            border.PointerEntered += (_, _) => border.Themed(Border.BackgroundProperty, Subtle);
            border.PointerExited += (_, _) => border.Background = Avalonia.Media.Brushes.Transparent;
            var line = b.Line;
            border.PointerReleased += (_, _) =>
            {
                flyout.Hide();
                GoToStepLine(line);
            };
            list.Children.Add(border);
        }

        flyout.ShowAt(anchor);
    }

    /// <summary>Scrolls to the step that starts at this file line (when it is among the steps shown).</summary>
    private void GoToStepLine(long line)
    {
        if (_readable?.Steps.FirstOrDefault(e => e.Line == line) is { } step)
        {
            _readable.Reveal(step);
        }
        else
        {
            _status.Text = "That step is older than the steps shown: use Show earlier steps";
        }
    }
}
