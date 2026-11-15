// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class MainWindow : Window
{
    private const double StartWidth = 1280, StartHeight = 820, MinimumWidth = 900;
    private const double OpenWidth = 240, ClosedWidth = 64;
    private readonly AppState _s = new();
    private readonly Border _side;
    private readonly ContentControl _body = new();
    private readonly Ctx _x;
    private readonly LibraryView _skills, _plugins;
    private readonly AppsView _apps;
    private readonly HealthView _health;
    private readonly McpView _mcp;
    private readonly UsageView _usage;
    private readonly AppDetailView _detail;
    private readonly ProjectView _project;
    private readonly SettingsView _settings;
    private readonly LogsView _logs;
    private string _view = "Dashboard";
    private bool _navOpen = true, _wasLogs, _scanning, _scanned;

    public MainWindow()
    {
        Title = "AI Stackprint"; Width = StartWidth; Height = StartHeight; MinWidth = MinimumWidth;
        using (var icon = typeof(MainWindow).Assembly.GetManifestResourceStream("app-icon.ico"))
        {
            if (icon != null)
            {
                Icon = new WindowIcon(icon);
            }
        }

        try { AppCatalog.MigrateOldFolder(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // old data stays where it was; the app starts fresh
        _s.Settings = SettingsStore.Load();
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        _x = new Ctx(_s, this, Go, Scan) { AddApp = AddApp, AddProject = AddProject, Restyle = Restyle, Refresh = Refresh, Open = OpenItem, Browse = Browse, Limits = new LimitsService(() => _s.Settings) };
        _skills = new LibraryView(_x, "Skill"); _plugins = new LibraryView(_x, "Plugin"); _apps = new AppsView(_x); _detail = new AppDetailView(_x);
        _project = new ProjectView(_x, RemoveProject); _settings = new SettingsView(_x); _logs = new LogsView(_x); _health = new HealthView(_x); _mcp = new McpView(_x); _usage = new UsageView(_x);
        _palette = new CommandPalette(_x, _overlay, Go);
        _side = new Border
        {
            Width = OpenWidth,
            ClipToBounds = true,
            Padding = new Thickness(Space.L, Space.L),
            Transitions = [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }]
        }.Themed(Border.BackgroundProperty, SideKey);
        var main = new DockPanel();
        main.Children.Add(_body);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,*") }.Themed(Panel.BackgroundProperty, MainKey);
        var bar = TitleBar();
        Grid.SetColumnSpan(bar, 2);
        Grid.SetRow(_side, 1); Grid.SetRow(main, 1);
        GoAction = Go;
        Grid.SetColumn(main, 1);
        root.Children.Add(bar); root.Children.Add(_side); root.Children.Add(main);
        Toast.Host = new StackPanel { Spacing = Space.M, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, Space.XXXL, Space.XXL) };
        Grid.SetColumnSpan(Toast.Host, 2); Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(Toast.Host, 2); Grid.SetRowSpan(_overlay, 2);
        root.Children.Add(Toast.Host); root.Children.Add(_overlay);
        Content = root;
        _body.Content = DashboardView.Loading(_x, AddProject, out var status);
        _status = status;   // the first scan: the dashboard's controls, each card with the loading icon
        Keys();
        Limits();
        // Plain text blocks cannot be selected: right-click copies the block's text (one shared menu, no per-block cost)
        AddHandler(PointerReleasedEvent, CopyMenu, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        ActualThemeVariantChanged += (_, _) => RefreshNav();   // OS theme change or sun/moon click
        Opened += (_, _) => { _perfOpened = System.Diagnostics.Stopwatch.GetTimestamp(); _s.Defs = AppCatalog.Load(); Scan(); };
    }

    private void CopyMenu(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right || e.Source is not TextBlock { Text.Length: > 0 } tb || tb is SelectableTextBlock)
        {
            return;
        }

        var item = new MenuItem { Header = "Copy" };
        item.Click += (_, _) => Toast.Copy(this, tb.Text, "Text");
        new ContextMenu { Items = { item } }.Open(tb);
        e.Handled = true;
    }

    /// <summary>Sidebar row: icon + label when open, icon with tooltip when collapsed.</summary>
    private Button NavBtn(string icon, string text, Action a, bool active = false)
    {
        var full = _navOpen;
        var glyph = Icon(icon, IconSize.L);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.L, Children = { glyph } };
        if (full)
        {
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = Sizes.NavItem,
            Background = Brushes.Transparent,
            HorizontalContentAlignment = full ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Padding = new Thickness(full ? Space.L : 0, 0),
            FontWeight = MenuWeight,
            FontSize = Size(TextSize.Body)
        };
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
        var app = Application.Current!;
        foreach (var v in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var d = new ResourceDictionary();
            foreach (var s in new[] { "PointerOver", "Pressed" })
            {
                if (app.TryGetResource("AccentButtonBackground" + s, v, out var bg))
                {
                    d["ButtonBackground" + s] = bg;
                }

                if (app.TryGetResource("AccentButtonForeground" + s, v, out var fg))
                {
                    d["ButtonForeground" + s] = fg;
                }
            }
            b.Resources.ThemeDictionaries[v] = d;
        }
    }

    private Button Go(string icon, string text, string view) => NavBtn(icon, text, () => Go(view), _view == view || _view.StartsWith(view == "Apps" ? "App:" : view + ":"));

    /// <summary>Views: Dashboard, Apps, App:&lt;id&gt;, Plugin, Skill, Mcp, Logs, Logs:&lt;id&gt;, Project:&lt;path&gt;, Health, Settings. Kept in the back/forward history.</summary>
    private void Go(string view)
    {
        if (view != _view) { _back.Push(_view); _forward.Clear(); }
        Show(view);
    }

    private void Show(string view)
    {
        var rescan = _view == "Settings" && view != "Settings" && _x.SettingsChanged;
        _view = view;
        if (rescan) { _x.SettingsChanged = false; Scan(); }
        else
        {
            Refresh();
        }
    }

    /// <summary>Skills or Plugins page filtered to an app and/or a search text.</summary>
    private void Browse(string kind, string? app, string? query)
    {
        (kind == "Plugin" ? _plugins : _skills).Filter(app, query);
        Go(kind);
    }

    /// <summary>Opens a skill or plugin on its page with the panel showing it.</summary>
    private void OpenItem(Item i)
    {
        if (i.Kind == "Plugin") { _plugins.Select(i); Go("Plugin"); }
        else if (i.Kind == "Skill") { _skills.Select(i); Go("Skill"); }
        else
        {
            Go("App:" + i.App);
        }
    }

    private Control Section(string title) => _navOpen
        ? Txt(title, TextSize.Tiny, false, Muted).With(t => t.Margin = new Thickness(Space.L, Space.XL, 0, Space.XS))
        : new Border { Height = Stroke.Thin, Margin = new Thickness(Space.M, Space.L) }.Themed(Border.BackgroundProperty, BorderKey);

    private void RefreshNav()
    {
        var nav = new StackPanel { Spacing = Space.XXS };
        nav.Children.Add(Go("board", "Dashboard", "Dashboard"));
        nav.Children.Add(Go("apps", "Apps", "Apps"));
        nav.Children.Add(Go("puzzle_piece", "Plugins", "Plugin"));
        nav.Children.Add(Go("sparkle", "Skills", "Skill"));
        nav.Children.Add(Go("server", "MCP servers", "Mcp"));
        nav.Children.Add(Go("data_pie", "Usage", "Usage"));
        nav.Children.Add(Go("document_text", "Logs", "Logs"));
        var warnings = _s.Health.Count(h => h.Finding.Warning);
        nav.Children.Add(Go("warning", warnings > 0 && _navOpen ? $"Health · {warnings}" : "Health", "Health"));
        nav.Children.Add(Section("PROJECTS"));
        foreach (var p in _s.Projects)
        {
            nav.Children.Add(Go("folder", Ctx.ProjectName(p), "Project:" + p).With(b => ToolTip.SetTip(b, p)));
        }

        nav.Children.Add(NavBtn("folder_add", "Add project folder", AddProject));

        var foot = new StackPanel { Spacing = Space.M, Margin = new Thickness(0, Space.L, 0, 0) };
        foot.Children.Add(Go("settings", "Settings", "Settings"));
        var rescan = Btn(_navOpen ? "Rescan" : "", Scan, true, "arrow_sync");
        if (_scanning && _navOpen)
        {
            rescan.Content = Busy.Inline(true, "Scanning…", OnAccent);   // the button is accent colored: spinner and text must be the on-accent color
        }

        if (!_navOpen) { rescan.Content = _scanning ? Busy.Inline(true, "", OnAccent) : Icon("arrow_sync", IconSize.L); rescan.Padding = new Thickness(0); ToolTip.SetTip(rescan, _scanning ? "Scanning…" : "Rescan"); }
        rescan.HorizontalAlignment = HorizontalAlignment.Stretch; rescan.HorizontalContentAlignment = HorizontalAlignment.Center;
        foot.Children.Add(rescan);

        var dock = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = nav });
        _side.Child = dock;
    }
}
