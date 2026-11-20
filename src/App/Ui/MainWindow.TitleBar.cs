// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Custom title bar: menu toggle, name and back/forward on the left, search in the middle, theme switch and window buttons on the right.
/// F11 toggles full screen (there is no full-screen button). On macOS the system window buttons stay.
/// </summary>
internal partial class MainWindow
{
    private const double MacButtons = 78;   // room for the macOS window buttons on the left
    private const double TitleBarHeight = 40;
    private Button _backBtn = null!, _forwardBtn = null!, _maxBtn = null!, _rescanBtn = null!;
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
        Button menu = null!;
        menu = TitleBtn("navigation", "Collapse menu", () =>
        {
            _navOpen = !_navOpen;
            _side.Width = _navOpen ? OpenWidth : ClosedWidth;
            ToolTip.SetTip(menu, _navOpen ? "Collapse menu" : "Expand menu");
            RefreshNav();
        });
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.XXS,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(mac ? MacButtons : Space.L, 0, 0, 0),   // macOS draws its window buttons on the left
            Children = { menu, Title("AI Stackprint", TextSize.Small).With(t => t.Margin = new Thickness(Space.S, 0, Space.L, 0)), _backBtn, _forwardBtn }
        };
        var search = Btn("Search · Ctrl+K", _palette.Open, false, "search").With(b =>
        {
            b.MinHeight = 0;
            b.Height = Sizes.CompactControl;
            b.Padding = new Thickness(Space.L, 0);
            b.Width = Widths.TitleSearch;
            b.VerticalAlignment = VerticalAlignment.Center;   // shorter than the bar, like an input in it
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Themed(BackgroundProperty, SideKey);
            ToolTip.SetTip(b, "Find apps, skills, plugins, projects and actions");
        });
        var row = new Grid
        {
            Height = TitleBarHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*")
        };   // the search box stays in the middle of the window
        Grid.SetColumn(search, 1);
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.XXS,
            Margin = new Thickness(0, 0, Space.M, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _rescanBtn = TitleBtn("scan_text", "Rescan (F5)", Scan);
        right.Children.Add(_rescanBtn);
        _themeHost.Content = ThemeSwitch();
        right.Children.Add(_themeHost);
        if (!mac)
        {
            _maxBtn = TitleBtn("maximize", "Maximize", ToggleMaximize);
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) { UpdateMaxButton(); } };
            right.Children.Add(new Border
            {
                Width = Stroke.Thin,
                Height = IconSize.M,
                Margin = new Thickness(Space.M, 0)
            }.Themed(Border.BackgroundProperty, BorderKey));   // theme | window buttons
            right.Children.Add(TitleBtn("subtract", "Minimize", () => WindowState = WindowState.Minimized));
            right.Children.Add(_maxBtn);
            right.Children.Add(DangerHover(TitleBtn("dismiss", "Close", Close)));
        }

        Grid.SetColumn(right, 2);
        row.Children.Add(left);
        row.Children.Add(search);
        row.Children.Add(right);
        var bar = new Border
        {
            Height = TitleBarHeight,
            Child = row,
            Background = Avalonia.Media.Brushes.Transparent
        }.Themed(Border.BackgroundProperty, MainKey);
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

    private readonly ContentControl _themeHost = new() { VerticalAlignment = VerticalAlignment.Center };

    private static readonly (string Mode, string Icon, string Tip)[] _themes =
        [("System", "desktop", "Follow the system theme"), ("Light", "weather_sunny", "Light theme"), ("Dark", "weather_moon", "Dark theme")];

    /// <summary>System / moon / sun in one rounded group at the right of the title bar; the chosen one is filled with the selection color.</summary>
    private Border ThemeSwitch()
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.XXS
        };
        foreach (var (mode, icon, tip) in _themes)
        {
            var on = mode == _s.Settings.Theme;
            var b = TitleBtn(icon, tip, () => SetTheme(mode));
            b.Padding = new Thickness(Space.S);
            if (on)
            {
                b.Themed(BackgroundProperty, Selected).Themed(ForegroundProperty, Accent);
            }

            Avalonia.Automation.AutomationProperties.SetName(b, tip + (on ? " (on)" : ""));
            group.Children.Add(b);
        }

        return new Border
        {
            Child = group,
            Padding = new Thickness(Space.XXS),
            CornerRadius = new CornerRadius(Radius.L + Space.XXS),
            BorderThickness = new Thickness(Stroke.Thin)
        }
            .Themed(Border.BorderBrushProperty, BorderKey);
    }

    /// <summary>Title-bar icon button: muted icon, Rescan colors (accent fill, accent text) under the pointer.</summary>
    private static Button TitleBtn(string icon, string tip, Action a)
    {
        var b = new Button
        {
            Content = Icon(icon, IconSize.S),
            Padding = new Thickness(Space.M),
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };   // no border: only the theme group has one
        b.Themed(ForegroundProperty, Muted);   // the icon inherits it, so the hover text color applies too
        HoverStyle(b);
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
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
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
            return;
        }
        _beforeFullScreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        WindowState = WindowState.FullScreen;
    }

    /// <summary>The Rescan button of the title bar: the spinner shows while a scan runs (a second click does nothing then).</summary>
    private void UpdateRescan()
    {
        if (_rescanBtn == null)
        {
            return;   // the title bar is not built yet
        }

        _rescanBtn.Content = _scanning ? new Panel
        {
            Width = IconSize.S,
            Height = IconSize.S,
            Children = { Busy.Spinner(IconSize.S, Accent) }
        } : Icon("scan_text", IconSize.S);
        ToolTip.SetTip(_rescanBtn, _scanning ? "Scanning…" : "Rescan (F5)");
    }

    private void UpdateMaxButton()
    {
        var max = WindowState == WindowState.Maximized;
        _maxBtn.Content = Icon(max ? "restore" : "maximize", IconSize.S);
        ToolTip.SetTip(_maxBtn, max ? "Restore" : "Maximize");
    }

    private void UpdateHistoryButtons()
    {
        _backBtn.IsEnabled = _back.Count > 0;
        _forwardBtn.IsEnabled = _forward.Count > 0;
    }
}
