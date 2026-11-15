// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Full-width list of skills or plugins with search, app and level filters, row actions (open folder, copy path, remove),
/// and a resizable bottom panel for the selected row. Used by the Skills and Plugins pages and the project page.
/// Keeps its search, filters and selection while the app runs.
/// </summary>
internal class ItemBrowser
{
    private const double MinPanel = 160, MaxPanel = 2000;   // bottom panel height limits
    /// <summary>How to search skills and plugins (<see cref="ItemSearch"/>), as the search box's tooltip.</summary>
    public const string SearchTip = "Words match separately; \"quotes\" match a phrase.\nname: desc: author: app: level: keyword: path: search one field, has:mcp (skills, commands, agents, hooks, mcp, lsp) what a plugin adds; author:\"Jane Doe\" matches the whole value.\nBest matches first: name, then keywords, author, description, path.";
    private static readonly TimeSpan _typing = TimeSpan.FromMilliseconds(150);   // search waits this long after the last key
    private readonly Ctx _x;
    private readonly string _kind;
    private readonly TextBox _search = SearchBox("Search, or author: app: level: keyword:");
    private readonly DispatcherTimer _wait = new() { Interval = _typing };
    private readonly ComboBox _app = new() { MinWidth = Widths.Field };
    private readonly SkillFilter _filter = new();
    private readonly DetailPanel _panel;
    private readonly ContentControl _table = new();
    private readonly ContentControl _own = new();   // the filter rows: rebuilt when a filter changes, so their counts follow the other filters
    private readonly TextBlock _shown = Txt("", TextSize.Small, false, Muted);
    private string _appId = "", _q = "";
    private Item? _sel;
    private bool _building, _max;
    private List<Item> _items = [];
    private ItemSearch _index = new([]);
    private SearchQuery _query = SearchQuery.Empty;
    private Grid? _grid;

    public ItemBrowser(Ctx ctx, string kind)
    {
        _x = ctx; _kind = kind; _panel = new DetailPanel(ctx);
        ToolTip.SetTip(_search, SearchTip);
        _wait.Tick += (_, _) => { _wait.Stop(); Fill(); };
        _search.TextChanged += (_, _) => { _q = _search.Text ?? ""; _wait.Stop(); _wait.Start(); };   // search after a short pause in typing
        _app.SelectionChanged += (_, _) => { if (_building) { return; } _appId = (_app.SelectedItem as AppOption)?.Id ?? ""; Fill(); };
    }

    /// <summary>Clears search, filters and selection (another project opened).</summary>
    public void Reset() { _sel = null; _appId = ""; _search.Text = ""; _q = ""; _filter.Reset(); _max = false; }

    /// <summary>Shows only one app's rows and/or rows matching a text (dashboard bars); other filters are cleared.</summary>
    public void Filter(string? app, string? query) { Reset(); _appId = app ?? ""; _search.Text = query ?? ""; _q = query ?? ""; }

    /// <summary>Selects an item so the panel opens on it the next time the page is shown.</summary>
    public void Select(Item i) { _sel = i; _max = false; }

    /// <param name="top">Page header and any sections above the list.</param>
    /// <param name="items">The rows in scope (this kind only).</param>
    public Control Build(IEnumerable<Control> top, List<Item> items)
    {
        _items = items;
        _index = new ItemSearch(items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase), ItemAuthor.For(_x.S.Items));   // lower-case text made once per scan, not per key
        if (_sel != null)
        {
            _sel = items.FirstOrDefault(i => Same(i, _sel));   // the same item after a rescan (a new record), or gone
        }

        _building = true;
        var apps = _x.ShownApps.Where(a => items.Any(i => i.App == a.Id)).Select(a => new AppOption(a.Id, a.Name)).Prepend(new AppOption("", "All apps")).ToList();
        _app.ItemsSource = apps;
        _app.SelectedItem = apps.FirstOrDefault(a => a.Id == _appId) ?? apps[0];
        if (_app.SelectedItem is AppOption { Id: "" })
        {
            _appId = "";
        }

        _building = false;

        _own.Content = _filter.Controls(items, _kind == "Skill", FilterChanged);
        var search = new DockPanel
        {
            Children = { _app.Free().With(c => { DockPanel.SetDock(c, Dock.Right); c.Margin = new Thickness(Space.L, 0, 0, 0); c.VerticalAlignment = VerticalAlignment.Center; }),
            _shown.Free().With(t => { DockPanel.SetDock(t, Dock.Right); t.Margin = new Thickness(Space.L, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }), _search.Free() }
        };
        var list = new StackPanel { Spacing = Space.XL, Margin = new Thickness(Space.Page, Space.XXXL, Space.Page, Space.XXL) };
        list.Children.AddRange(top);
        list.Children.Add(search);
        list.Children.Add(_own.Free());
        list.Children.Add(Card(_table.Free()));
        Fill();

        var g = _grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,0") };
        _splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Height = Sizes.Splitter }.Themed(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, BorderKey);
        ToolTip.SetTip(_splitter, "Drag to resize · double-click to maximize or restore");
        _splitter.DragCompleted += (_, _) => { if (!_max) { _x.S.Settings.PanelHeight = Math.Round(g.RowDefinitions[2].ActualHeight); SettingsStore.Save(_x.S.Settings); } };
        _splitter.DoubleTapped += (_, _) => { _max = !_max; Layout(); };
        _bottom = new Border().Themed(Border.BackgroundProperty, Background);
        Grid.SetRow(_splitter, 1); Grid.SetRow(_bottom, 2);
        g.Children.Add(new ScrollViewer { Content = list }); g.Children.Add(_splitter); g.Children.Add(_bottom);
        ShowPanel();
        _x.Escape = () => { if (_sel == null) { return false; } Close(); return true; };
        return g;
    }

    private GridSplitter? _splitter;
    private Border? _bottom;

    /// <summary>Opens, switches or closes the panel in place, so the list keeps its scroll position.</summary>
    private void ShowPanel()
    {
        if (_bottom == null || _splitter == null)
        {
            return;
        }

        _bottom.Child = _sel == null ? null : _panel.Build(_sel, Close).Free();   // the panel outlives the page: take it from the old one
        _splitter.IsVisible = _bottom.IsVisible = _sel != null;
        Layout();
    }

    private void Layout()
    {
        if (_grid == null)
        {
            return;
        }

        var open = _sel != null;
        _grid.RowDefinitions[0].Height = open && _max ? new GridLength(0) : GridLength.Star;
        _grid.RowDefinitions[2].Height = !open ? new GridLength(0) : _max ? GridLength.Star : new GridLength(Math.Clamp(_x.S.Settings.PanelHeight, MinPanel, MaxPanel));
    }

    private void Close() { _sel = null; _max = false; ShowPanel(); Fill(); }

    private void Pick(Item i) { _sel = _sel != null && Same(i, _sel) ? null : i; if (_sel == null) { _max = false; } ShowPanel(); Fill(); }

    /// <summary>The same skill or plugin across scans. Records compare lists by reference, so a rescan would make every item look new.</summary>
    public static bool Same(Item a, Item b) => a.Kind == b.Kind && a.App == b.App && a.Level == b.Level && a.Name == b.Name && a.Path == b.Path && a.Project == b.Project;

    private sealed record AppOption(string Id, string Name) { public override string ToString() => Name; }

    private void FilterChanged()
    {
        _own.Content = _filter.Controls(_items, _kind == "Skill", FilterChanged);
        Fill();
    }

    private void Fill()
    {
        _wait.Stop();
        _query = SearchQuery.Parse(_q);
        var shown = _index.Find(_query).Where(i => (_appId == "" || i.App == _appId) && _filter.Match(i)).ToList();   // best matches first
        _shown.Text = shown.Count == _items.Count ? $"{_items.Count:N0}" : $"{shown.Count:N0} of {_items.Count:N0}";

        var cols = _kind == "Plugin"
            // minimums fit the narrowest window (900 px): Path gives way first
            ? new[] { new Col("Plugin", "2*", Min: 120), new Col("App", "1.3*", Min: 116), new Col("Level", "1.1*", Min: 56), new Col("Version", ".7*", Min: 56), new Col("Path", "3*"), new Col("", "112") }
            : [new Col("Skill", "2.2*", Min: 120), new Col("App", "1.3*", Min: 116), new Col("Level", "1.1*", Min: 56), new Col("Invoked by", ".9*", Min: 76), new Col("Path", "3*"), new Col("", "112")];
        _table.Content = Table.Build(cols, _filter.Rows(shown, Row, cols.Length, Fill),
            _items.Count == 0 ? $"No {(_kind == "Plugin" ? "plugins" : "skills")} found." : "Nothing matches. Clear the search or filters.");
    }

    private TableRow Row(Item i) => new(
    [
        Table.NameDesc(i.Name, i.Description, marks: _query),
        DetailPanel.AppLink(_x, i.App, i.Source),
        AppDetailView.LevelCell(i),
        _kind == "Plugin" ? Txt(i.Version ?? "", TextSize.Small) : AppDetailView.InvokeCell(i),
        PathCell(i.Path),
        Actions(i)
    ], () => Pick(i), _sel != null && Same(i, _sel));

    private StackPanel Actions(Item i)
    {
        var sp = (StackPanel)W.Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(i.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(_x.Owner, i.Path)));
        sp.Spacing = 0;
        if (i.Kind == "Skill" && SkillManager.CannotRemove(i) == null)
        {
            sp.Children.Add(IconBtn("delete", "Remove skill", () => AppActions.RemoveSkill(_x, i)));
        }
        else if (i.Kind == "Plugin" && _x.S.Def(i.App) is { PluginCommands: not null } d)
        {
            sp.Children.Add(IconBtn("delete", "Remove plugin", () => AppActions.RemovePlugin(_x, d, i)));
        }

        return sp;
    }

    /// <summary>The whole path, wrapped, never cut: the folder part muted, the name part (skill folder + file) in the emphasis weight.</summary>
    public static Control PathCell(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        var parts = path.TrimEnd(sep).Split(sep);
        var keep = parts.Length >= 2 && parts[^1].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        var head = string.Join(sep, parts[..^keep]) + (parts.Length > keep ? sep.ToString() : "");
        var tb = new TextBlock
        {
            FontFamily = Theme.Mono,
            FontSize = Size(TextSize.Caption),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxLines = 4,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        }   /* a squeezed column (narrow window) would otherwise wrap a letter per line; the tooltip has the full path */.Themed(TextBlock.ForegroundProperty, Text);
        tb.Inlines =
        [
            new Run(head).With(r => r.Bind(TextElement.ForegroundProperty, tb.GetResourceObservable(Muted))),
            new Run(string.Join(sep, parts[^keep..])) { FontWeight = Emphasis }
        ];
        ToolTip.SetTip(tb, path);
        return tb;
    }
}
