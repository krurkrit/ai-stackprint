// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;

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
    private readonly LibraryView _skills, _plugins, _commands, _agents;
    private readonly AppsView _apps;
    private readonly HealthView _health;
    private readonly McpView _mcp;
    private readonly HooksView _hooks;
    private readonly UsageView _usage;
    private readonly ModelsView _models;
    private readonly AppDetailView _detail;
    private readonly ProjectView _project;
    private readonly SettingsView _settings;
    private readonly LogsView _logs;
    private string _view = "Dashboard";
    private bool _navOpen = true, _wasLogs, _scanning, _scanned;
    private readonly Border _frame = null!;

    public MainWindow()
    {
        Title = "AI Stackprint";
        Width = StartWidth;
        Height = StartHeight;
        MinWidth = MinimumWidth;
        using (var icon = typeof(MainWindow).Assembly.GetManifestResourceStream("app-icon.ico"))
        {
            if (icon != null)
            {
                Icon = new WindowIcon(icon);
            }
        }

        _s.Settings = SettingsStore.Load();   // first: it hands values older versions kept there to state.json
        Table.VirtualFrom = _s.Settings.VirtualListRows;
        _s.State = StateStore.Load();
        LoadProjects();
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        _x = new Ctx(_s, this, Go, Scan)
        {
            AddApp = AddApp,
            AddProject = AddProject,
            Restyle = Restyle,
            Reload = Reload,
            Refresh = Refresh,
            Open = OpenItem,
            Browse = Browse,
            Limits = new LimitsService(() => _s.Settings)
        };
        _skills = new LibraryView(_x, "Skill");
        _plugins = new LibraryView(_x, "Plugin");
        _commands = new LibraryView(_x, "Command");
        _agents = new LibraryView(_x, "Agent");
        _apps = new AppsView(_x);
        _detail = new AppDetailView(_x);
        _project = new ProjectView(_x, RemoveProject, RelocateProject);
        _settings = new SettingsView(_x);
        _logs = new LogsView(_x);
        _health = new HealthView(_x);
        _mcp = new McpView(_x);
        _hooks = new HooksView(_x);
        _usage = new UsageView(_x);
        _models = new ModelsView(_x);
        _palette = new CommandPalette(_x, _overlay, Go);
        _side = new Border
        {
            Width = OpenWidth,
            ClipToBounds = true,
            Padding = new Thickness(Space.L, Space.L),
            BorderThickness = new Thickness(0, 0, Stroke.Thin, 0),   // a line between the side menu and the page
            Transitions = [new DoubleTransition
            {
                Property = WidthProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut()
            }]
        }.Themed(Border.BackgroundProperty, SideKey).Themed(Border.BorderBrushProperty, BorderKey);
        var main = new DockPanel();
        main.Children.Add(_body);
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,*")
        }.Themed(Panel.BackgroundProperty, MainKey);
        var bar = TitleBar();
        Grid.SetColumnSpan(bar, 2);
        Grid.SetRow(_side, 1);
        Grid.SetRow(main, 1);
        GoAction = Go;
        Grid.SetColumn(main, 1);
        root.Children.Add(bar);
        root.Children.Add(_side);
        root.Children.Add(main);
        Toast.Host = new StackPanel
        {
            Spacing = Space.M,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, Space.XXXL, Space.XXL)
        };
        Grid.SetColumnSpan(Toast.Host, 2);
        Grid.SetColumnSpan(_overlay, 2);
        Grid.SetRowSpan(Toast.Host, 2);
        Grid.SetRowSpan(_overlay, 2);
        root.Children.Add(Toast.Host);
        root.Children.Add(_overlay);
        _frame = new Border
        {
            BorderThickness = new Thickness(Stroke.Thin),
            Child = root
        }.Themed(Border.BorderBrushProperty, BorderKey);   // a square 1 px line around the whole window
        Content = _frame;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == OffScreenMarginProperty)
            {
                FitFrame();
            }
        };
        FitFrame();
        _body.Content = DashboardView.Loading(out var status);
        _status = status;   // the first scan: the dashboard's controls, each card with the loading icon
        Keys();
        Limits();
        // Right-click on any text copies it (the selection, or the whole block): also for text in buttons and clickable rows, where a drag cannot select (one shared menu, no per-block cost)
        AddHandler(PointerReleasedEvent, CopyMenu, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        ActualThemeVariantChanged += (_, _) => RefreshNav();   // OS theme change or sun/moon click
        Opened += (_, _) =>
        {
            _perfOpened = System.Diagnostics.Stopwatch.GetTimestamp();
            _s.Use(AppCatalog.Read());
            Scan();
        };
    }

    /// <summary>The line stays inside the screen when the window is maximized (Windows draws a maximized window a few pixels beyond the screen edge).</summary>
    private void FitFrame() => _frame.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : new Thickness(0);

    private void CopyMenu(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right || e.Source is not TextBlock { Text.Length: > 0 } tb)
        {
            return;
        }

        // Selected text copies just the selection; otherwise the block's whole text
        var chosen = tb is SelectableTextBlock { SelectedText.Length: > 0 } sel ? sel.SelectedText : tb.Text;
        var item = new MenuItem { Header = tb is SelectableTextBlock { SelectedText.Length: > 0 } ? "Copy selection" : "Copy" };
        item.Click += (_, _) => Toast.Copy(this, chosen, "Text");
        new ContextMenu { Items = { item } }.Open(tb);
        e.Handled = true;
    }
}
