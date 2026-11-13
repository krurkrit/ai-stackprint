using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Back/forward history, keyboard and mouse shortcuts, and the limits refresh timer.</summary>
partial class MainWindow
{
    readonly Stack<string> _back = new(), _forward = new();
    readonly Panel _overlay = new();
    readonly CommandPalette _palette;

    void Back() { if (_back.Count == 0) return; _forward.Push(_view); Show(_back.Pop()); }
    void Forward() { if (_forward.Count == 0) return; _back.Push(_view); Show(_forward.Pop()); }

    static readonly (string Mode, string Icon, string Tip)[] Themes =
        { ("System", "desktop", "Follow the system theme"), ("Light", "weather_sunny", "Light theme"), ("Dark", "weather_moon", "Dark theme") };

    /// <summary>System / sun / moon switch. Open: three segments, the active one highlighted. Collapsed: one button that cycles.</summary>
    Control ThemeSwitch()
    {
        var cur = _s.Settings.Theme;
        if (!_navOpen)
        {
            int i = Math.Max(0, Array.FindIndex(Themes, t => t.Mode == cur));
            var next = Themes[(i + 1) % Themes.Length];
            return NavBtn(Themes[i].Icon, $"{Themes[i].Tip} (click: {next.Mode.ToLowerInvariant()})", () => SetTheme(next.Mode));
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        for (int i = 0; i < Themes.Length; i++)
        {
            var t = Themes[i]; bool on = t.Mode == cur;
            var b = new Button { Content = Icon(t.Icon, 18), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 32, Background = Brushes.Transparent };
            HoverStyle(b);
            b.Themed(Button.ForegroundProperty, on ? Accent : Muted);
            if (on) b.Themed(Button.BackgroundProperty, Surface);
            ToolTip.SetTip(b, t.Tip);
            b.Click += (_, _) => SetTheme(t.Mode);
            Grid.SetColumn(b, i);
            g.Children.Add(b);
        }
        return g;
    }

    void SetTheme(string mode)
    {
        _s.Settings.Theme = mode;
        SettingsStore.Save(_s.Settings);
        Restyle();
    }

    void Keys()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (ctrl && e.Key == Key.K) { _palette.Open(); e.Handled = true; }
            else if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
            else if (e.Key == Key.F5) { if (_view.StartsWith("Logs")) _logs.Reload(); else Scan(); e.Handled = true; }
            else if (e.Key == Key.Escape && _palette.IsOpen) { _palette.Close(); e.Handled = true; }
            else if (e.Key == Key.Escape && _x.Escape?.Invoke() == true) e.Handled = true;
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) { Back(); e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) { Forward(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Mouse back/forward buttons
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1) { Back(); e.Handled = true; }
            else if (e.InitialPressMouseButton == MouseButton.XButton2) { Forward(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Asks for limits of the shown, installed apps that have a provider; the service decides whether a call is due (refresh interval, at most once a minute).
    /// Paused while the window is minimized.
    /// </summary>
    void Limits()
    {
        _x.Limits.Updated += _ => Dispatcher.UIThread.Post(() => LimitsChanged?.Invoke());
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => RequestLimits());
        timer.Start();
        Opened += (_, _) => RequestLimits();
    }

    /// <summary>Raised on the UI thread when any app's limits change; the page showing them refreshes its card.</summary>
    public static event Action? LimitsChanged;

    void RequestLimits()
    {
        if (WindowState == WindowState.Minimized || !_scanned) return;
        foreach (var d in _s.Defs.Where(d => d.Limits != null && _x.Info(d.Id)?.Installed == true))
            _x.Limits.Request(d, _s.Projects);
    }
}
