// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Input;

namespace AiStackprint.Ui;

/// <summary>Back/forward history, keyboard and mouse shortcuts, and the limits refresh timer.</summary>
internal partial class MainWindow
{
    private readonly Stack<string> _back = new(), _forward = new();
    private readonly Panel _overlay = new();
    private readonly CommandPalette _palette;

    private void Back()
    {
        if (_back.Count == 0) { return; }
        _forward.Push(_view);
        Show(_back.Pop());
    }
    private void Forward()
    {
        if (_forward.Count == 0) { return; }
        _back.Push(_view);
        Show(_forward.Pop());
    }

    private void SetTheme(string mode)
    {
        _s.Settings.Theme = mode;
        SettingsStore.Save(_s.Settings);
        Restyle();
        _themeHost.Content = ThemeSwitch();
    }

    private void Keys()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (ctrl && e.Key == Key.K)
            {
                _palette.Open();
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                if (_view.StartsWith("Logs"))
                {
                    _logs.Reload();
                }
                else
                {
                    Scan();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _palette.IsOpen)
            {
                _palette.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _x.Escape?.Invoke() == true)
            {
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left)
            {
                Back();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right)
            {
                Forward();
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Mouse back/forward buttons
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1)
            {
                Back();
                e.Handled = true;
            }
            else if (e.InitialPressMouseButton == MouseButton.XButton2)
            {
                Forward();
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private LimitsTimer _limitsTimer = null!;

    /// <summary>
    /// Limits are asked for by the app's <see cref="LimitsTimer"/> (<see cref="AppHost"/>), with or without this window. A window with no host (tests) has its own,
    /// paused while it is minimized or before the first scan.
    /// </summary>
    private void Limits()
    {
        _limitsTimer = _host?.Timer ?? new LimitsTimer(_x.Limits, () => LimitsTimer.Apps(_s, _x.Limits.Enabled), () => _s.Projects,
            () => WindowState == WindowState.Minimized || !_scanned);
        _limitsTimer.Changed += OnLimits;
        Closed += (_, _) => _limitsTimer.Changed -= OnLimits;
        Opened += (_, _) => RequestLimits();
    }

    private static void OnLimits(string appId) => LimitsChanged?.Invoke();

    /// <summary>Raised on the UI thread when any app's limits change; the page showing them refreshes its card.</summary>
    public static event Action? LimitsChanged;

    private void RequestLimits() => _limitsTimer.Request();
}
