// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The Bookmarks button: the errors of this file and the steps you starred or wrote a note on; a click goes to the step.</summary>
internal partial class LogViewer
{
    private StepContext? _marks;   // what the steps of the open file can do: bookmarks (not while the file is shown cut to its end) and links

    private void ReadMarks() => _marks = _file == null ? null : new StepContext(_x.S.State, _file.Path, _linesOk, Link);

    /// <summary>Where a thing a step points at is shown in the app: a model on the Models page, a skill on the Skills page, a server on the Servers page; a file opens in its program.</summary>
    private (string Label, Action Go)? Link(StepRef r) => r.Kind switch
    {
        "model" => ($"Model · {r.Value}", () => _x.Go("Models:" + (ModelCatalog.PricedAs(r.Value) ?? r.Value))),
        "skill" => ($"Skill · {r.Value}", () => _x.Browse("Skill", null, r.Value)),
        "mcp" => ($"Server · {r.Value}", () => _x.Go("Mcp")),
        "file" when Path.IsPathRooted(r.Value) && File.Exists(r.Value) => ($"File · {Path.GetFileName(r.Value)}", () => PathUtil.OpenFile(r.Value)),
        _ => null
    };

    /// <summary>The toolbar button; null when no session is shown as steps.</summary>
    private Button? BookmarksButton()
    {
        if (!ShowReadable)
        {
            return null;
        }

        var button = IconBtn("bookmark", "Errors, bookmarks and notes of this file", () => { });
        button.Click += (_, _) => ShowBookmarks(button);
        return button;
    }

    private void ShowBookmarks(Control anchor)
    {
        var flyout = new Flyout();
        flyout.Content = new ScrollViewer { MaxHeight = Heights.Viewer, Content = MarksList(flyout.Hide) };
        flyout.ShowAt(anchor);
    }

    /// <summary>The popup's content: the errors (a failed tool result, or an error line of a text log), then the bookmarks and notes.</summary>
    internal StackPanel MarksList(Action close)
    {
        var list = new StackPanel { Spacing = Space.XXS, Margin = new Thickness(Space.M), Width = Widths.Popup };
        var errors = _readable?.Steps.Where(e => e.IsError).ToList() ?? [];
        if (errors.Count > 0)
        {
            list.Children.Add(Txt($"Errors ({errors.Count:N0})", TextSize.Small, true, Muted));
            foreach (var e in errors)
            {
                var step = e;
                list.Children.Add(MarkRow("warning", Txt(Preview(e), TextSize.Small).With(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis), () => _readable?.Reveal(step), close));
            }
        }

        if (IsLines)
        {
            if (errors.Count == 0)
            {
                list.Children.Add(Txt("No errors in this file.", TextSize.Small, false, Muted));
            }

            return list;
        }

        var marks = _marks?.All() ?? [];
        list.Children.Add(Txt("Bookmarks and notes", TextSize.Small, true, Muted).With(t => t.Margin = new Thickness(0, errors.Count > 0 ? Space.S : 0, 0, 0)));
        if (_marks is not { CanMark: true })
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

            var line = b.Line;
            list.Children.Add(MarkRow(b.Starred ? "bookmark_filled" : "document_edit", row, () => GoToStepLine(line), close));
        }

        return list;
    }

    private static string Preview(LogEntry e)
    {
        var text = (e.Text ?? "").Split((char)10).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        var line = e.Line is > 0 ? $"Line {e.Line:N0} · " : "";
        return line + (e.Tool != null && e.Role != "tool" ? e.Tool + " · " : "") + (text.Length > 160 ? text[..160] : text);
    }

    /// <summary>One clickable row of the popup: an icon and a text; the click closes the popup and goes to the step.</summary>
    private static Border MarkRow(string icon, Control content, Action go, Action close)
    {
        var border = new Border
        {
            Child = Row(Icon(icon, IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Accent), content),
            Padding = new Thickness(Space.S),
            CornerRadius = new CornerRadius(Radius.S),
            Cursor = Hand,
            Background = Avalonia.Media.Brushes.Transparent
        };
        border.PointerEntered += (_, _) => border.Themed(Border.BackgroundProperty, Subtle);
        border.PointerExited += (_, _) => border.Background = Avalonia.Media.Brushes.Transparent;
        border.PointerReleased += (_, _) =>
        {
            close();
            go();
        };
        return border;
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
