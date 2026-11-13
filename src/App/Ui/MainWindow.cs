using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

partial class MainWindow : Window
{
    const double OpenWidth = 240, ClosedWidth = 64;
    readonly AppState _s = new();
    readonly Border _side;
    readonly ContentControl _body = new();
    readonly Ctx _x;
    readonly LibraryView _skills, _plugins;
    readonly AppsView _apps;
    readonly HealthView _health;
    readonly AppDetailView _detail;
    readonly ProjectView _project;
    readonly SettingsView _settings;
    readonly LogsView _logs;
    string _view = "Dashboard";
    bool _navOpen = true, _wasLogs, _scanning, _scanned;

    public MainWindow()
    {
        Title = "AI Stackprint"; Width = 1280; Height = 820; MinWidth = 900;
        using (var icon = typeof(MainWindow).Assembly.GetManifestResourceStream("app-icon.ico")) if (icon != null) Icon = new WindowIcon(icon);
        try { AppCatalog.MigrateOldFolder(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // old data stays where it was; the app starts fresh
        _s.Settings = SettingsStore.Load();
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        _x = new Ctx(_s, this, Go, Scan) { AddApp = AddApp, AddProject = AddProject, Restyle = Restyle, Refresh = Refresh, Open = OpenItem, Browse = Browse, Limits = new LimitsService(() => _s.Settings) };
        _skills = new LibraryView(_x, "Skill"); _plugins = new LibraryView(_x, "Plugin"); _apps = new AppsView(_x); _detail = new AppDetailView(_x);
        _project = new ProjectView(_x, RemoveProject); _settings = new SettingsView(_x); _logs = new LogsView(_x); _health = new HealthView(_x);
        _palette = new CommandPalette(_x, _overlay, Go);
        _side = new Border
        {
            Width = OpenWidth,
            ClipToBounds = true,
            Padding = new Thickness(10, 12),
            Transitions = new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() } }
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
        Toast.Host = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 20) };
        Grid.SetColumnSpan(Toast.Host, 2); Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(Toast.Host, 2); Grid.SetRowSpan(_overlay, 2);
        root.Children.Add(Toast.Host); root.Children.Add(_overlay);
        Content = root;
        _body.Content = Busy.Loading("Scanning apps, skills and plugins…");
        Keys();
        Limits();
        // Plain text blocks cannot be selected: right-click copies the block's text (one shared menu, no per-block cost)
        AddHandler(PointerReleasedEvent, CopyMenu, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        ActualThemeVariantChanged += (_, _) => RefreshNav();   // OS theme change or sun/moon click
        Opened += (_, _) => { _s.Defs = AppCatalog.Load(); Scan(); };
    }

    void CopyMenu(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right || e.Source is not TextBlock { Text.Length: > 0 } tb || tb is SelectableTextBlock) return;
        var item = new MenuItem { Header = "Copy" };
        item.Click += (_, _) => Toast.Copy(this, tb.Text, "Text");
        new ContextMenu { Items = { item } }.Open(tb);
        e.Handled = true;
    }

    /// <summary>Sidebar row: icon + label when open, icon with tooltip when collapsed.</summary>
    Button NavBtn(string icon, string text, Action a, bool active = false, bool iconOnly = false)
    {
        bool full = _navOpen && !iconOnly;
        var glyph = Icon(icon, 20);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { glyph } };
        if (full) content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40,
            Background = Brushes.Transparent,
            HorizontalContentAlignment = full ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Padding = new Thickness(full ? 12 : 0, 0),
            FontWeight = MenuWeight,
            FontSize = Size(14)
        };
        if (active)
        {
            b.Themed(Button.BackgroundProperty, Surface).Themed(Button.ForegroundProperty, Accent);
            b.CornerRadius = new CornerRadius(8);
        }
        HoverStyle(b);
        if (!full) ToolTip.SetTip(b, text);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Pointer over and pressed: exactly the accent (Rescan) button's own hover and pressed colors.</summary>
    static void HoverStyle(Button b)
    {
        b.CornerRadius = new CornerRadius(8);
        var app = Application.Current!;
        foreach (var v in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var d = new ResourceDictionary();
            foreach (var s in new[] { "PointerOver", "Pressed" })
            {
                if (app.TryGetResource("AccentButtonBackground" + s, v, out var bg)) d["ButtonBackground" + s] = bg;
                if (app.TryGetResource("AccentButtonForeground" + s, v, out var fg)) d["ButtonForeground" + s] = fg;
            }
            b.Resources.ThemeDictionaries[v] = d;
        }
    }

    Button Go(string icon, string text, string view) => NavBtn(icon, text, () => Go(view), _view == view || _view.StartsWith(view == "Apps" ? "App:" : view + ":"));

    /// <summary>Views: Dashboard, Apps, App:&lt;id&gt;, Plugin, Skill, Logs, Logs:&lt;id&gt;, Project:&lt;path&gt;, Health, Settings. Kept in the back/forward history.</summary>
    void Go(string view)
    {
        if (view != _view) { _back.Push(_view); _forward.Clear(); }
        Show(view);
    }

    void Show(string view)
    {
        bool rescan = _view == "Settings" && view != "Settings" && _x.SettingsChanged;
        _view = view;
        if (rescan) { _x.SettingsChanged = false; Scan(); } else Refresh();
    }

    /// <summary>Skills or Plugins page filtered to an app and/or a search text.</summary>
    void Browse(string kind, string? app, string? query)
    {
        (kind == "Plugin" ? _plugins : _skills).Filter(app, query);
        Go(kind);
    }

    /// <summary>Opens a skill or plugin on its page with the panel showing it.</summary>
    void OpenItem(Item i)
    {
        if (i.Kind == "Plugin") { _plugins.Select(i); Go("Plugin"); }
        else if (i.Kind == "Skill") { _skills.Select(i); Go("Skill"); }
        else Go("App:" + i.App);
    }

    Control Section(string title) => _navOpen
        ? Txt(title, 11, false, Muted).With(t => t.Margin = new Thickness(12, 16, 0, 4))
        : new Border { Height = 1, Margin = new Thickness(8, 12) }.Themed(Border.BackgroundProperty, BorderKey);

    void RefreshNav()
    {
        var head = new StackPanel { Orientation = _navOpen ? Orientation.Horizontal : Orientation.Vertical, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        var toggle = NavBtn("navigation", _navOpen ? "Collapse menu" : "Expand menu", () => { _navOpen = !_navOpen; _side.Width = _navOpen ? OpenWidth : ClosedWidth; RefreshNav(); }, iconOnly: true);
        toggle.Width = 44;
        head.Children.Add(toggle);
        if (_navOpen)
        {
            var box = Btn("Search · Ctrl+K", _palette.Open, false, "search").With(b => { b.MinHeight = 36; b.Width = 160; b.Themed(Button.BackgroundProperty, MainKey); b.HorizontalContentAlignment = HorizontalAlignment.Left; ToolTip.SetTip(b, "Find apps, skills, plugins, projects and actions"); });
            head.Children.Add(box);
        }
        else head.Children.Add(IconBtn("search", "Search (Ctrl+K)", _palette.Open, Text).With(b => b.HorizontalAlignment = HorizontalAlignment.Center));

        var nav = new StackPanel { Spacing = 2 };
        nav.Children.Add(Go("board", "Dashboard", "Dashboard"));
        nav.Children.Add(Go("apps", "AI apps", "Apps"));
        nav.Children.Add(Go("puzzle_piece", "Plugins", "Plugin"));
        nav.Children.Add(Go("sparkle", "Skills", "Skill"));
        nav.Children.Add(Go("document_text", "Logs", "Logs"));
        int warnings = _s.Health.Count(h => h.Finding.Warning);
        nav.Children.Add(Go("warning", warnings > 0 && _navOpen ? $"Health · {warnings}" : "Health", "Health"));
        nav.Children.Add(Section("PROJECTS"));
        foreach (var p in _s.Projects)
            nav.Children.Add(Go("folder", Ctx.ProjectName(p), "Project:" + p).With(b => ToolTip.SetTip(b, p)));
        nav.Children.Add(NavBtn("folder_add", "Add project folder", AddProject));

        var foot = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        foot.Children.Add(Go("settings", "Settings", "Settings"));
        foot.Children.Add(ThemeSwitch());
        var rescan = Btn(_navOpen ? "Rescan" : "", Scan, true, "arrow_sync");
        if (_scanning && _navOpen) rescan.Content = Busy.Inline(true, "Scanning…");
        if (!_navOpen) { rescan.Content = _scanning ? Busy.Inline(true, "") : Icon("arrow_sync", 20); rescan.Padding = new Thickness(0); ToolTip.SetTip(rescan, _scanning ? "Scanning…" : "Rescan"); }
        rescan.HorizontalAlignment = HorizontalAlignment.Stretch; rescan.HorizontalContentAlignment = HorizontalAlignment.Center;
        foot.Children.Add(rescan);

        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(head); dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = nav });
        _side.Child = dock;
    }

    /// <summary>Re-applies Appearance settings and rebuilds the UI so sizes and weights take effect.</summary>
    void Restyle()
    {
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        Refresh();
    }

    void Refresh()
    {
        if (!_scanned) return;   // the first scan shows its own loading line
        RefreshNav();
        UpdateHistoryButtons();
        _x.Escape = null;
        bool inLogs = _view == "Logs" || _view.StartsWith("Logs:");
        bool leftLogs = _wasLogs && !inLogs;
        _wasLogs = inLogs;
        if (leftLogs) _logs.Release();   // the Logs page keeps no file text, controls or watcher once it is closed
        _body.Content = _view switch
        {
            "Dashboard" => DashboardView.Build(_x, AddProject),
            "Apps" => _apps.Build(),
            "Settings" => _settings.Build(),
            "Plugin" => _plugins.Build(),
            "Skill" => _skills.Build(),
            "Health" => _health.Build(),
            "Logs" => _logs.Build(null),
            _ when _view.StartsWith("Logs:") => _logs.Build(_view[5..]),
            _ when _view.StartsWith("App:") => _detail.Build(_view[4..]),
            _ when _view.StartsWith("Project:") => _project.Build(_view[8..]),
            _ => DashboardView.Build(_x, AddProject)
        };
        MemoryTrim.Request();   // the page that was just replaced
    }

    async void AddProject()
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project folder" });
        if (r.Count == 0 || r[0].TryGetLocalPath() is not { } p || _s.Projects.Contains(p)) return;
        _s.Projects.Add(p);
        _view = "Project:" + p;
        Scan();
    }

    void RemoveProject(string p)
    {
        _s.Projects.Remove(p);
        _view = "Dashboard";
        Scan();
    }

    async void AddApp()
    {
        var def = await new AddAppDialog().ShowDialog<AppDefinition?>(this);
        if (def == null) return;
        AppCatalog.SaveUser(def);
        _s.Defs = AppCatalog.Load();
        _view = "App:" + def.Id;
        Scan();
    }

    async void Scan()
    {
        if (_scanning) return;
        _scanning = true; RefreshNav();
        var defs = SettingsStore.Visible(_s.Defs, _s.Settings); var projects = _s.Projects.ToList();
        var r = await Task.Run(() => Scanner.Collect(defs, projects));
        _s.Items = r.Items; _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health) = await Task.Run(() => (ScanDiff.Update(items, ScanDiff.File), Health.Compute(items)));
        bool first = !_scanned;
        _scanning = false; _scanned = true;
        Refresh();
        RequestLimits();
        if (first && CrashLog.TakeNew()) Toast.Show("AI Stackprint closed unexpectedly last time. The details are in " + CrashLog.File, "warning", 12);
    }
}
