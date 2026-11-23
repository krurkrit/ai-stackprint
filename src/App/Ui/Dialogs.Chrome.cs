// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class Dialogs
{
    /// <summary>The outer width of a dialog window whose card is <paramref name="card"/> wide: the card plus the room its shadow needs on both sides.</summary>
    internal static double OuterWidth(double card) => card + Sizes.ShadowRoom + Sizes.ShadowRoom;

    /// <summary>
    /// Makes <paramref name="w"/> a Fluent UI 2 dialog: no system frame, a floating card (rounded corners, a thin border, a deep soft shadow) with the title and a close button on top,
    /// the body, and the buttons at the bottom right. The card is dragged by its title, and Esc or the close button closes with <c>false</c>.
    /// </summary>
    internal static void Chrome(Window w, string title, Control body, double card, params Button[] buttons) => Chrome(w, false, title, body, card, buttons);

    /// <param name="cancel">What Esc and the close button close the dialog with: <c>false</c> for a dialog that answers yes or no, <c>null</c> for one that answers with an object (a cast of <c>false</c> to it would throw).</param>
    internal static void Chrome(Window w, object? cancel, string title, Control body, double card, params Button[] buttons)
    {
        w.Title = title;
        w.WindowDecorations = WindowDecorations.None;
        w.Background = Brushes.Transparent;
        w.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        w.SizeToContent = SizeToContent.Height;
        w.Width = OuterWidth(card);
        w.CanResize = false;
        w.ShowInTaskbar = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var close = IconBtn("dismiss", "Close", () => w.Close(cancel), danger: true);
        var header = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(Title(title, TextSize.Title));
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                w.BeginMoveDrag(e);
            }
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var b in buttons)
        {
            bar.Children.Add(b);
        }

        w.Content = new Border
        {
            Margin = new Thickness(Sizes.ShadowRoom),
            Padding = new Thickness(Space.XXXL),
            CornerRadius = new CornerRadius(Radius.XL),
            BorderThickness = new Thickness(Stroke.Thin),
            BoxShadow = Theme.DialogShadow(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark),
            Child = new StackPanel
            {
                Spacing = Space.XL,
                Children = { header, body, bar }
            }
        }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                w.Close(cancel);
            }
        };
    }
}
