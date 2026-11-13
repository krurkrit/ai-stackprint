// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Short confirmations ("Path copied") at the bottom right; they fade out on their own. MainWindow sets the host.</summary>
internal static class Toast
{
    public static StackPanel? Host { get; set; }

    public static void Show(string text, string icon = "info", double seconds = 2.4)
    {
        if (Host == null)
        {
            return;
        }

        var t = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = Row(Icon(icon, 16).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Accent), Txt(text, 13)),
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }]
        }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
        Host.Children.Add(t);
        while (Host.Children.Count(c => c.Tag is not Pinned) > 3)
        {
            Host.Children.Remove(Host.Children.First(c => c.Tag is not Pinned));
        }

        DispatcherTimer.RunOnce(() => t.Opacity = 0, TimeSpan.FromSeconds(seconds));
        DispatcherTimer.RunOnce(() => Host?.Children.Remove(t), TimeSpan.FromSeconds(seconds + 0.6));
    }

    private const string Pinned = "pinned";

    /// <summary>Adds a card that stays until <see cref="Unpin"/> (e.g. the update notice); short toasts do not push it out.</summary>
    public static void Pin(Control card)
    {
        card.Tag = Pinned;
        Host?.Children.Add(card);
    }

    public static void Unpin(Control? card)
    {
        if (card != null)
        {
            Host?.Children.Remove(card);
        }
    }

    /// <summary>Copies text to the clipboard and confirms it.</summary>
    public static async void Copy(TopLevel owner, string text, string what = "Path")
    {
        if (owner.Clipboard is not { } c)
        {
            return;
        }

        try { await c.SetTextAsync(text); }
        catch (System.Runtime.InteropServices.ExternalException) { Show("Could not copy: the clipboard is busy. Try again.", "warning"); return; }
        Show($"{what} copied", "copy");
    }
}
