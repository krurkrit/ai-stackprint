// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Custom title bar: name and back/forward on the left, minimize and close on the right.
/// F11 toggles full screen (there is no full-screen button). On macOS the system window buttons stay.
/// </summary>
internal partial class MainWindow
{
    private const double TitleBarHeight = 40;
    private Button _backBtn = null!, _forwardBtn = null!, _maxBtn = null!;
    private WindowState _beforeFullScreen = WindowState.Normal;

    private Border TitleBar()
    {
        var mac = OperatingSystem.IsMacOS();
        ExtendClientAreaToDecorationsHint = true;
        if (mac)
        {
            ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        }
        else
        {
            WindowDecorations = WindowDecorations.BorderOnly;   // our own buttons replace the drawn caption buttons
        }

        _backBtn = HistoryBtn(TitleBtn("arrow_left", "Back (Alt+←)", Back));
        _forwardBtn = HistoryBtn(TitleBtn("arrow_right", "Forward (Alt+→)", Forward));
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(mac ? 78 : 14, 0, 0, 0),   // macOS draws its window buttons on the left
            Children = { Title("AI Stackprint", 13).With(t => t.Margin = new Thickness(0, 0, 10, 0)), _backBtn, _forwardBtn }
        };
        var row = new DockPanel { Height = TitleBarHeight };
        if (!mac)
        {
            _maxBtn = TitleBtn("maximize", "Maximize", ToggleMaximize);
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) { UpdateMaxButton(); } };
            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { TitleBtn("subtract", "Minimize", () => WindowState = WindowState.Minimized), _maxBtn, CloseHover(TitleBtn("dismiss", "Close", Close)) }
            };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }
        row.Children.Add(left);
        var bar = new Border { Height = TitleBarHeight, Child = row, Background = Avalonia.Media.Brushes.Transparent }.Themed(Border.BackgroundProperty, MainKey);
        // Drag and double-click handled here, not through Avalonia's title-bar role: with that role Windows took some button clicks as drags.
        // Buttons handle their own presses, so this only sees presses on the empty bar.
        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                if (!mac)
                {
                    ToggleMaximize();
                }
            }
            else
            {
                BeginMoveDrag(e);
            }

            e.Handled = true;
        };
        UpdateHistoryButtons();
        return bar;
    }

    /// <summary>Title-bar icon button: muted icon, Rescan colors (accent fill, accent text) under the pointer.</summary>
    private static Button TitleBtn(string icon, string tip, Action a)
    {
        var b = new Button { Content = Icon(icon, 16), Padding = new Thickness(8), Background = Avalonia.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        b.Themed(ForegroundProperty, Muted);   // the icon inherits it, so the hover text color applies too
        HoverStyle(b);
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Close turns the danger-button red under the pointer, like the Windows close button.</summary>
    private static Button CloseHover(Button b)
    {
        foreach (var v in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            if (Application.Current!.Resources.ThemeDictionaries.TryGetValue(v, out var td) && td is ResourceDictionary rd && rd.TryGetValue(ErrorFill, out var red) && rd.TryGetValue(OnError, out var onRed))
            {
                b.Resources.ThemeDictionaries[v] = new ResourceDictionary
                {
                    ["ButtonBackgroundPointerOver"] = red,
                    ["ButtonBackgroundPressed"] = red,
                    ["ButtonForegroundPointerOver"] = onRed,
                    ["ButtonForegroundPressed"] = onRed
                };
            }
        }

        return b;
    }

    /// <summary>Disabled back/forward: no gray fill, only a faded icon.</summary>
    private static Button HistoryBtn(Button b)
    {
        b.Resources["ButtonBackgroundDisabled"] = Avalonia.Media.Brushes.Transparent;
        b.Resources["ButtonBorderBrushDisabled"] = Avalonia.Media.Brushes.Transparent;
        b.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty && b.Content is Control c) { c.Opacity = b.IsEnabled ? 1 : .35; } };
        return b;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen) { WindowState = _beforeFullScreen; return; }
        _beforeFullScreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        WindowState = WindowState.FullScreen;
    }

    private void UpdateMaxButton()
    {
        var max = WindowState == WindowState.Maximized;
        _maxBtn.Content = Icon(max ? "restore" : "maximize", 16);
        ToolTip.SetTip(_maxBtn, max ? "Restore" : "Maximize");
    }

    private void UpdateHistoryButtons()
    {
        _backBtn.IsEnabled = _back.Count > 0;
        _forwardBtn.IsEnabled = _forward.Count > 0;
    }
}
