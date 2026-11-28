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

        var header = Header(title, () => w.Close(cancel));
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

        var column = new StackPanel { Spacing = Space.XL, Children = { header, body } };
        if (buttons.Length > 0)
        {
            column.Children.Add(bar);   // a dialog without buttons (the × and Esc close it) has no empty row
        }

        var shown = Card(column);
        w.Content = shown;
        w.Opened += (_, _) => Motion.Enter(shown, Duration.Normal, scale: ZoomFrom);
        LeaveSoftly(w, shown);
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                w.Close(cancel);
            }
        };
    }

    private const double ZoomFrom = .96;   // a dialog grows from 96 % as it fades in, and shrinks back as it fades out

    /// <summary>
    /// The card fades out before the window closes: the first close is held, the motion runs, then the window closes for real with the result
    /// the first close gave (the window keeps it). Closing with the owner or the app is not held.
    /// </summary>
    private static void LeaveSoftly(Window w, Control card)
    {
        var leaving = false;
        w.Closing += (_, e) =>
        {
            if (leaving || !Motion.On || e.CloseReason != WindowCloseReason.WindowClosing)
            {
                return;
            }

            e.Cancel = true;
            leaving = true;
            Fire.Forget(LeaveAsync());
        };

        async Task LeaveAsync()
        {
            await Motion.Leave(card, Duration.Fast, scale: ZoomFrom);
            w.Close();
        }
    }

    /// <summary>A dialog's title row: the title, and the close button at the right.</summary>
    internal static DockPanel Header(string title, Action close)
    {
        var x = IconBtn("dismiss", "Close", close, danger: true);
        var header = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(x, Dock.Right);
        header.Children.Add(x);
        header.Children.Add(Title(title, TextSize.Title));
        return header;
    }

    /// <summary>The floating card of a dialog (and of an ⓘ pop-up): rounded, a thin border, the dialog shadow and room around it for the shadow.</summary>
    internal static Border Card(Control child) => new Border
    {
        Margin = new Thickness(Sizes.ShadowRoom),
        Padding = new Thickness(Space.XXXL),
        CornerRadius = new CornerRadius(Radius.XL),
        BorderThickness = new Thickness(Stroke.Thin),
        BoxShadow = Theme.DialogShadow(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark),
        Child = child
    }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
}
