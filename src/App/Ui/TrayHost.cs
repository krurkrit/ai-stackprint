// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>
/// The tray icon while "Keep running in the tray" is on: the ring of the most-used limit (the app icon with no reading), one line per app in the tooltip,
/// and the menu: the usage line, Open AI Stackprint, Show widget, Pause alerts for 1 hour, Settings, Quit. A left click opens the window (on macOS the menu opens, as is the convention there).
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly AppHost _h;
    private readonly TrayIcon _icon;
    private (int Percent, bool Dark, uint Accent)? _drawn;

    public TrayHost(AppHost h)
    {
        _h = h;
        _icon = new TrayIcon { Icon = TrayIconArt.App(), ToolTipText = "AI Stackprint", IsVisible = true };
        _icon.Clicked += (_, _) =>
        {
            if (!OperatingSystem.IsMacOS())
            {
                h.Open(null);
            }
        };
        TrayIcon.SetIcons(Application.Current!, [_icon]);
        Update();
    }

    /// <summary>The ring, the tooltip and the menu from the latest readings; the ring is drawn again only when the percent, the taskbar theme or the accent changed.</summary>
    public void Update()
    {
        var apps = _h.WidgetApps(forTray: true);
        var top = WidgetRows.MostUsed(apps);
        var dark = TrayIconArt.TaskbarDark();
        if (top is { } t)
        {
            var key = ((int)Math.Round(t.Used), dark, TrayIconArt.Color(W.Accent, dark).ToUInt32());
            if (_drawn != key)
            {
                _drawn = key;
                _icon.Icon = TrayIconArt.Ring(t.Used, dark);
            }
        }
        else if (_drawn != null)
        {
            _drawn = null;
            _icon.Icon = TrayIconArt.App();
        }

        _icon.ToolTipText = WidgetRows.Tooltip(apps);
        _icon.Menu = Menu(top?.App, Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
    }

    public NativeMenu? CurrentMenu => _icon.Menu;

    private NativeMenu Menu(WidgetApp? top, bool dark)
    {
        var s = _h.S.Settings;
        var paused = _h.AlertsPaused;
        var m = new NativeMenu();
        m.Items.Add(new NativeMenuItem(top != null ? WidgetRows.Summary(top) : "No usage limits read yet") { IsEnabled = false });
        m.Items.Add(new NativeMenuItemSeparator());
        m.Items.Add(Item("window", "Open AI Stackprint", dark, () => _h.Open(null)));
        m.Items.Add(Item("text_bullet_list", "Show widget", dark, () => _h.ShowWidget(!s.WidgetEnabled)).With(i =>
        {
            i.ToggleType = MenuItemToggleType.CheckBox;
            i.IsChecked = s.WidgetEnabled;
        }));
        m.Items.Add(Item("alert_off", paused is { } until ? $"Resume alerts (paused until {until.ToLocalTime():HH:mm})" : "Pause alerts for 1 hour", dark, () => _h.PauseAlerts(paused == null)));
        m.Items.Add(Item("settings", "Settings", dark, () => _h.Open("Settings:Usage limits")));
        m.Items.Add(new NativeMenuItemSeparator());
        m.Items.Add(Item("sign_out", "Quit AI Stackprint", dark, _h.Quit));
        return m;
    }

    private static NativeMenuItem Item(string icon, string text, bool dark, Action click)
    {
        return new NativeMenuItem(text) { Icon = TrayIconArt.Glyph(icon, dark), Command = new Act(click) };
    }

    /// <summary>A menu item's action (a command, so tests can run it as a click does).</summary>
    private sealed class Act(Action a) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => a();
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        TrayIcon.SetIcons(Application.Current!, []);
        _icon.Dispose();
    }
}
