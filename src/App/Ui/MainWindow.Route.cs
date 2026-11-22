// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class MainWindow
{
    /// <summary>Sidebar row: icon + label when open, icon with tooltip when collapsed.</summary>
    private Button NavBtn(string icon, string text, Action a, bool active = false)
    {
        var full = _navOpen;
        var glyph = Icon(icon, IconSize.L);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.L,
            Children = { glyph }
        };
        if (full)
        {
            content.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = Sizes.NavItem,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = full ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Padding = new Thickness(full ? Space.L : 0, 0),
            FontWeight = MenuWeight,
            FontSize = Size(TextSize.Body * MenuScale)
        };
        if (MenuFont != null)
        {
            b.FontFamily = MenuFont;
        }

        if (active)
        {
            b.Themed(BackgroundProperty, Surface).Themed(ForegroundProperty, Accent);
            b.CornerRadius = new CornerRadius(Radius.L);
        }
        HoverStyle(b);
        if (!full)
        {
            ToolTip.SetTip(b, text);
        }

        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Pointer over and pressed: exactly the accent (Rescan) button's own hover and pressed colors.</summary>
    private static void HoverStyle(Button b)
    {
        b.CornerRadius = new CornerRadius(Radius.L);
        AccentHover(b);
    }

    private Button Go(string icon, string text, string view) => NavBtn(icon, text, () => Go(view), _view == view || _view.StartsWith(view == "Apps" ? "App:" : view + ":"));

    /// <summary>Views: Dashboard, Apps, App:&lt;id&gt;, Plugin, Skill, Part (commands and agents), Mcp, Usage, Models, Models:&lt;name&gt;, Logs, Logs:&lt;id&gt;, Project:&lt;path&gt;, Health, Settings (opens Settings:&lt;page&gt;), About. Kept in the back/forward history.</summary>
    private void Go(string view)
    {
        if (view == "Settings")
        {
            view = "Settings:" + _lastSetting;   // the menu's Settings button opens the subpage seen last
        }

        if (view != _view)
        {
            _back.Push(_view);
            _forward.Clear();
        }
        Show(view);
    }

    private void Show(string view)
    {
        var rescan = InSettings(_view) && !InSettings(view) && _x.SettingsChanged;
        if (InSettings(view))
        {
            if (!InSettings(_view))
            {
                _beforeSettings = _view;   // where Back to app returns to
            }

            if (view.StartsWith("Settings:"))
            {
                _lastSetting = view[9..];
            }
        }

        _view = view;
        if (rescan)
        {
            _x.SettingsChanged = false;
            Scan();
        }
        else
        {
            Refresh();
        }
    }

    /// <summary>Skills or Plugins page filtered to an app and/or a search text.</summary>
    private void Browse(string kind, string? app, string? query)
    {
        (kind switch { "Plugin" => _plugins, "Part" => _parts, _ => _skills }).Filter(app, query);
        Go(kind);
    }

    /// <summary>Opens a skill or plugin on its page with the panel showing it.</summary>
    private void OpenItem(Item i)
    {
        if (i.Kind == "Plugin")
        {
            _plugins.Select(i);
            Go("Plugin");
        }
        else if (i.Kind == "Skill")
        {
            _skills.Select(i);
            Go("Skill");
        }
        else if (i.Kind is "Command" or "Agent")
        {
            _parts.Select(i);
            Go("Part");
        }
        else
        {
            Go("App:" + i.App);
        }
    }

    private Control Section(string title) => _navOpen
        ? Txt(title, TextSize.Tiny, false, Muted).With(t => t.Margin = new Thickness(Space.L, Space.XL, 0, Space.XS))
        : new Border
        {
            Height = Stroke.Thin,
            Margin = new Thickness(Space.M, Space.L)
        }.Themed(Border.BackgroundProperty, BorderKey);

    private void RefreshNav()
    {
        if (InSettings(_view))
        {
            RefreshSettingsNav();
            return;
        }

        var nav = new StackPanel { Spacing = Space.XXS };
        nav.Children.Add(Go("board", "Dashboard", "Dashboard"));
        nav.Children.Add(Go("apps", "Apps", "Apps"));
        nav.Children.Add(Go("puzzle_piece", "Plugins", "Plugin"));
        nav.Children.Add(Go("sparkle", "Skills", "Skill"));
        nav.Children.Add(Go("bot", "Commands & Agents", "Part"));
        nav.Children.Add(Go("server", "Servers", "Mcp"));
        nav.Children.Add(Go("flash", "Hooks", "Hooks"));
        nav.Children.Add(Go("data_pie", "Usage", "Usage"));
        nav.Children.Add(Go("brain_circuit", "Models", "Models"));
        nav.Children.Add(Go("document_text", "Logs", "Logs"));
        var warnings = _s.HealthReady ? _s.AllHealth.Count(h => h.Finding.Warning) : 0;   // no count until the projects are checked: it would be too low
        nav.Children.Add(Go("heart_pulse", warnings > 0 && _navOpen ? $"Health · {warnings}" : "Health", "Health"));
        nav.Children.Add(Section("PROJECTS"));
        foreach (var p in _s.Projects)
        {
            nav.Children.Add(ProjectRow(p));
        }

        nav.Children.Add(NavBtn("folder_add", "Add project folder", AddProject));

        var foot = new StackPanel
        {
            Spacing = Space.M,
            Margin = new Thickness(0, Space.L, 0, 0)
        };
        foot.Children.Add(Go("settings", "Settings", "Settings"));
        UpdateRescan();   // the title bar's button

        var dock = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = nav });
        _side.Child = dock;
    }
}
