// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// A limit alert in our own small window at the bottom right, where no system notifier can be reached (macOS and Linux for now, and Windows when a toast fails).
/// It looks like docs/mockups/tray-alerts.png; several stack upward, and each closes 10 seconds after the pointer last left it.
/// </summary>
internal sealed class AlertPopup : Window
{
    public static readonly TimeSpan Stay = TimeSpan.FromSeconds(10);
    private static readonly List<AlertPopup> _open = [];
    private readonly DispatcherTimer _timer;

    private AlertPopup(Alert a, Action<string> message)
    {
        Title = a.Title;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.Height;
        Width = Widths.Widget;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Content = TrayNotice.Card(Body(a, m =>
        {
            message(m);
            Close();
        }));
        _timer = new DispatcherTimer(Stay, DispatcherPriority.Background, (_, _) => Close());
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => _timer.Start();
        Opened += (_, _) =>
        {
            Stack();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _open.Remove(this);
            Stack();
        };
    }

    public static void Show(Alert a, Action<string> message)
    {
        var p = new AlertPopup(a, message);
        _open.Add(p);
        p.Show();
    }

    /// <summary>The content: the ring, "AI Stackprint" and "now"; the title; the text; and the buttons. Also drawn by the screenshot tests.</summary>
    public static StackPanel Body(Alert a, Action<string> message)
    {
        var dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, Space.XS) };
        var now = Txt("now", TextSize.Caption, false, Muted);
        DockPanel.SetDock(now, Dock.Right);
        head.Children.Add(now);
        head.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            Children =
            {
                new Image { Source = TrayIconArt.Draw((int)Math.Round(Sizes.NoticeRing * 2), 75, TrayIconArt.Color(Accent, dark), TrayIconArt.Color(Text, dark)), Width = Sizes.NoticeRing, Height = Sizes.NoticeRing },
                Txt("AI Stackprint", TextSize.Caption, false, Muted)
            }
        });
        var sp = new StackPanel { Spacing = Space.XXS, Children = { head, Txt(a.Title, TextSize.Small, true), Txt(a.Text, TextSize.Small, false, Muted) } };
        if (a.Open || a.Pause)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S, Margin = new Thickness(0, Space.XS, 0, 0) };
            if (a.Open)
            {
                buttons.Children.Add(Btn("Open", () => message("show App:" + a.AppId), primary: true));
            }

            if (a.Pause)
            {
                buttons.Children.Add(Btn("Pause alerts for 1 hour", () => message("pause")));
            }

            sp.Children.Add(buttons);
        }

        return sp;
    }

    /// <summary>The newest at the bottom right; the others above it, <see cref="Space.M"/> apart.</summary>
    private static void Stack()
    {
        double above = 0;
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var p = _open[i];
            if ((p.Screens.ScreenFromWindow(p) ?? p.Screens.Primary) is not { } screen || p.Bounds.Height == 0)
            {
                continue;
            }

            var k = screen.Scaling;
            var area = screen.WorkingArea;
            p.Position = new PixelPoint(area.Right - (int)Math.Ceiling((p.Bounds.Width + Sizes.ScreenEdge) * k),
                area.Bottom - (int)Math.Ceiling((p.Bounds.Height + Sizes.ScreenEdge + above) * k));
            above += p.Bounds.Height + Space.M;
        }
    }
}
