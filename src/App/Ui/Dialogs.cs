// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Modal dialogs: confirm, message, and a small form (optional text + one choice).</summary>
internal static partial class Dialogs
{
    private static Window Shell(string title, Control body, params Button[] buttons)
    {
        var w = new Window { Title = title, Width = Widths.DialogM, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons)
        {
            bar.Children.Add(b);
        }

        w.Content = new StackPanel { Margin = new Thickness(Space.XXL), Spacing = Space.XL, Children = { Txt(title, TextSize.Title, true), body, bar } };
        return w;
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string ok, bool danger = false)
    {
        Window? w = null;
        var yes = danger ? DangerBtn(ok, () => w!.Close(true)) : Btn(ok, () => w!.Close(true), true).With(b => b.IsDefault = true);   // Enter never confirms a destructive action
        w = Shell(title, Txt(message), Btn("Cancel", () => w!.Close(false)).With(b => b.IsCancel = true), yes);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>Shows text that is about to be written, read-only, and asks to write it. True when confirmed.</summary>
    public static async Task<bool> Preview(Window owner, string title, string intro, string text, string ok)
    {
        Window? w = null;
        var body = new StackPanel { Spacing = Space.M, Children = { Txt(intro, TextSize.Small, false, Muted), new ScrollViewer { MaxHeight = Heights.Viewer, Content = Mono(text, TextSize.Small) } } };
        w = Shell(title, body, Btn("Cancel", () => w!.Close(false)).With(b => b.IsCancel = true), Btn(ok, () => w!.Close(true), true));
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>A message with Close; <paramref name="warn"/> puts a warning icon before the text.</summary>
    public static Task Message(Window owner, string title, string text, bool mono = false, bool warn = false)
    {
        Window? w = null;
        Control body = mono ? new ScrollViewer { MaxHeight = Heights.ViewerS, Content = Mono(text == "" ? "(no output)" : text) } : Txt(text);
        if (warn)
        {
            var icon = Icon("warning", IconSize.M).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningKey).With(i => i.VerticalAlignment = VerticalAlignment.Top);
            DockPanel.SetDock(icon, Dock.Left);
            icon.Margin = new Thickness(0, 0, Space.L, 0);
            body = new DockPanel { Children = { icon, body } };
        }

        w = Shell(title, body, Btn("Close", () => w!.Close(), true).With(b => { b.IsDefault = true; b.IsCancel = true; }));
        return w.ShowDialog(owner);
    }

    /// <summary>Asks for an optional text value and one of <paramref name="choices"/>. Returns null on cancel.</summary>
    public static async Task<(string Text, int Choice)?> Ask(Window owner, string title, string intro, string? textLabel, string choiceLabel, string[] choices, string ok)
    {
        Window? w = null;
        var text = new TextBox { PlaceholderText = textLabel ?? "" };
        var pick = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var body = new StackPanel { Spacing = Space.M, Children = { Txt(intro, TextSize.Small, false, Muted) } };
        if (textLabel != null)
        {
            body.Children.Add(text);
        }

        body.Children.Add(Txt(choiceLabel, TextSize.Caption, false, Muted));
        body.Children.Add(pick);
        var go = Btn(ok, () => { if (textLabel == null || !string.IsNullOrWhiteSpace(text.Text)) { w!.Close(true); } }, true);
        go.IsDefault = true;
        w = Shell(title, body, Btn("Cancel", () => w!.Close(false)).With(b => b.IsCancel = true), go);
        return await w.ShowDialog<bool>(owner) ? ((text.Text ?? "").Trim(), pick.SelectedIndex) : null;
    }
}
