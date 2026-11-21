// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>While Settings is open the side menu lists its subpages: Back to app on top, About at the bottom (where Settings is on the other pages).</summary>
internal partial class MainWindow
{
    private string _lastSetting = SettingsView.Pages[0].Name, _beforeSettings = "Dashboard";

    private static bool InSettings(string view) => view is "Settings" or "About" || view.StartsWith("Settings:");

    private string SettingsPage() => _view == "About" ? "About" : _view.StartsWith("Settings:") ? _view[9..] : _lastSetting;

    private void RefreshSettingsNav()
    {
        var nav = new StackPanel { Spacing = Space.XXS };
        nav.Children.Add(NavBtn("arrow_left", "Back to app", () => Go(_beforeSettings)));
        nav.Children.Add(Section("SETTINGS"));
        foreach (var (name, icon) in SettingsView.Pages)
        {
            nav.Children.Add(Go(icon, name, "Settings:" + name));
        }

        var foot = new StackPanel { Margin = new Avalonia.Thickness(0, Space.L, 0, 0), Children = { Go("info", "About", "About") } };
        UpdateRescan();
        var dock = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = nav });
        _side.Child = dock;
    }
}
