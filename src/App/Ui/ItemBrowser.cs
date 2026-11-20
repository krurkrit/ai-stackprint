// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Full-width list of skills or plugins with search, app and level filters, row actions (open folder, copy path, remove),
/// and a resizable bottom panel for the selected row. Used by the Skills and Plugins pages and the project page.
/// Keeps its search, filters and selection while the app runs.
/// </summary>
internal partial class ItemBrowser
{
    private const double MinPanel = 160, MaxPanel = 2000;   // bottom panel height limits
    /// <summary>How to search skills and plugins (<see cref="ItemSearch"/>), as the search box's tooltip.</summary>
    internal const string SearchTip = "Words match separately; \"quotes\" match a phrase.\nname: desc: author: app: level: keyword: path: search one field, has:mcp (skills, commands, agents, hooks, mcp, lsp) what a plugin adds; author:\"Jane Doe\" matches the whole value.\nBest matches first: name, then keywords, author, description, path.";
    private static readonly TimeSpan _typing = TimeSpan.FromMilliseconds(150);   // search waits this long after the last key
    private readonly Ctx _x;
    private readonly string _kind;
    private readonly DispatcherTimer _wait = new() { Interval = _typing };
    private readonly SkillFilter _filter = new();
    private readonly DetailPanel _panel;
    // Kept while the page is shown (rebuilt rows keep the box's focus and the list its place); new ones once it is left: see Leave
    private TextBox _search = null!;
    private ComboBox _app = null!;
    private ContentControl _table = null!;
    private ContentControl _own = null!;   // the filter rows: rebuilt when a filter changes, so their counts follow the other filters
    private TextBlock _shown = null!;
    private string _appId = "", _q = "";
    private Item? _sel;
    private bool _building, _max;
    private List<Item> _items = [];
    private ItemSearch _index = new([]);
    private SearchQuery _query = SearchQuery.Empty;
    private Grid? _grid;

    public ItemBrowser(Ctx ctx, string kind)
    {
        _x = ctx;
        _kind = kind;
        _panel = new DetailPanel(ctx);
        _wait.Tick += (_, _) =>
        {
            _wait.Stop();
            Fill();
        };
        Leave();
    }

    /// <summary>
    /// The page was left: new controls for the next visit, so the old ones (and through them the whole old page) can be freed.
    /// Avalonia keeps a removed control linked to its old page's drawing tree, so a control kept in a field keeps that page in memory.
    /// </summary>
    public void Leave()
    {
        _wait.Stop();
        var search = _search = SearchBox("Search, or author: app: level: keyword:");
        search.Text = _q;
        ToolTip.SetTip(search, SearchTip);
        // search after a short pause in typing; the text set from _q is no change, and an old box says nothing
        search.TextChanged += (_, _) =>
        {
            if (search == _search && (search.Text ?? "") != _q)
            {
                _q = search.Text ?? "";
                _wait.Stop();
                _wait.Start();
            }
        };
        var app = _app = new ComboBox { MinWidth = Widths.Field };
        app.SelectionChanged += (_, _) =>
        {
            if (_building || app != _app) { return; }
            _appId = (app.SelectedItem as AppOption)?.Id ?? "";
            Fill();
        };
        _table = new();
        _own = new();
        _shown = Txt("", TextSize.Small, false, Muted);
        _grid = null;
        _splitter = null;
        _bottom = null;
        _filter.Leave();
        _panel.Leave();
    }

    /// <summary>Clears search, filters and selection (another project opened).</summary>
    public void Reset()
    {
        _sel = null;
        _appId = "";
        _q = "";
        _search.Text = "";
        _filter.Reset();
        _max = false;
    }

    /// <summary>Shows only one app's rows and/or rows matching a text (dashboard bars); other filters are cleared.</summary>
    public void Filter(string? app, string? query)
    {
        Reset();
        _appId = app ?? "";
        _q = query ?? "";
        _search.Text = _q;
    }

    /// <summary>Selects an item so the panel opens on it the next time the page is shown.</summary>
    public void Select(Item i)
    {
        _sel = i;
        _max = false;
    }

    /// <param name="top">Page header and any sections above the list.</param>
    /// <param name="items">The rows in scope (this kind only; "Part" = commands and agents).</param>
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

        _own.Content = _filter.Controls(items, _kind is "Skill" or "Part", FilterChanged);
        var search = new DockPanel
        {
            Children = { _app.Free().With(c =>
            {
                DockPanel.SetDock(c, Dock.Right);
                c.Margin = new Thickness(Space.L, 0, 0, 0);
                c.VerticalAlignment = VerticalAlignment.Center;
            }),
            _shown.Free().With(t =>
            {
                DockPanel.SetDock(t, Dock.Right);
                t.Margin = new Thickness(Space.L, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
            }), _search.Free() }
        };
        var list = new StackPanel
        {
            Spacing = Space.XL,
            Margin = new Thickness(Space.Page, Space.XXXL, Space.Page, Space.XXL)
        };
        list.Children.AddRange(top);
        list.Children.Add(search);
        list.Children.Add(_own.Free());
        list.Children.Add(Card(_table.Free()));
        Fill();

        var g = _grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,0") };
        _splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Rows,
            Height = Sizes.Splitter
        }.Themed(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, BorderKey);
        ToolTip.SetTip(_splitter, "Drag to resize · double-click to maximize or restore");
        _splitter.DragCompleted += (_, _) =>
        {
            if (!_max)
            {
                _x.S.State.PanelHeight = Math.Round(g.RowDefinitions[2].ActualHeight);
                StateStore.Save(_x.S.State);
            }
        };
        _splitter.DoubleTapped += (_, _) =>
        {
            _max = !_max;
            Layout();
        };
        _bottom = new Border().Themed(Border.BackgroundProperty, Background);
        Grid.SetRow(_splitter, 1);
        Grid.SetRow(_bottom, 2);
        g.Children.Add(new ScrollViewer { Content = list });
        g.Children.Add(_splitter);
        g.Children.Add(_bottom);
        ShowPanel();
        _x.Escape = Escape;   // a method, not a lambda here: that would share the closure that holds this page
        return g;
    }

    private bool Escape()
    {
        if (_sel == null) { return false; }
        Close();
        return true;
    }

    private GridSplitter? _splitter;
    private Border? _bottom;
}
