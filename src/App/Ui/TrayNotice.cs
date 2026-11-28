// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// "Still running in the tray": our own small window (the same on every platform), shown once, the first time × hides the main window.
/// It sits at the bottom right of the work area and closes on Got it, or 20 seconds after the pointer last left it.
/// </summary>
internal sealed class TrayNotice : Window
{
    public static readonly TimeSpan Stay = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer _timer;

    public TrayNotice(Action quit)
    {
        Title = "AI Stackprint";
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.Height;
        Width = Widths.Notice;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Content = Card(Body(quit));
        _timer = new DispatcherTimer(Stay, DispatcherPriority.Background, (_, _) => Close());
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => _timer.Start();
        Opened += (_, _) =>
        {
            Place(this, Sizes.ScreenEdge);
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private StackPanel Body(Action quit)
    {
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            Children =
            {
                new Image { Source = TrayIconArt.Draw((int)Math.Round(Sizes.NoticeRing * 2), 75, TrayIconArt.Color(Accent, Dark), TrayIconArt.Color(Text, Dark)), Width = Sizes.NoticeRing, Height = Sizes.NoticeRing },
                Txt("AI Stackprint", TextSize.Caption, false, Muted)
            }
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.S,
            Margin = new Thickness(0, Space.XS, 0, 0),
            Children = { Btn("Got it", Close, primary: true), Btn("Quit now", quit) }
        };
        return new StackPanel
        {
            Spacing = Space.XXS,
            Children =
            {
                head.With(h => h.Margin = new Thickness(0, 0, 0, Space.XS)),
                Txt("Still running in the tray", TextSize.Small, true),
                Txt("Usage is still checked and alerts still come. Click the tray icon to open the window; Quit is in its menu.", TextSize.Small, false, Muted),
                buttons
            }
        };
    }

    private static bool Dark => Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    /// <summary>The widget's card: the surface color, a thin border and the pop-up radius.</summary>
    public static Border Card(Control body) => new Border
    {
        CornerRadius = new CornerRadius(Radius.L),
        BorderThickness = new Thickness(Stroke.Thin),
        Padding = new Thickness(Space.L),
        Child = body
    }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);

    /// <summary>Puts <paramref name="w"/> at the bottom right of the work area of the screen it is on, <paramref name="edge"/> px (scaled) from its edges.</summary>
    public static void Place(Window w, double edge)
    {
        if ((w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary) is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        var k = screen.Scaling;
        w.Position = new PixelPoint(area.Right - (int)Math.Ceiling((w.Bounds.Width + edge) * k), area.Bottom - (int)Math.Ceiling((w.Bounds.Height + edge) * k));
    }
}
