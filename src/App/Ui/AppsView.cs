// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Flat list of apps with search, status and category filters, grouped by category with collapsible headings (kept while the app runs).
/// Clicking a row opens the app detail page.
/// </summary>
internal partial class AppsView(Ctx x)
{
    private readonly HashSet<string> _offCats = [], _offStatus = [], _collapsed = [];   // unchecked categories and statuses; collapsed headings
    private string _q = "";
    private TextBox _search = SearchBox("Search apps");
    private ContentControl _table = new();
    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave()
    {
        _search = SearchBox("Search apps");
        _search.Text = _q;
        _table = new();
        _statusMenu = _catMenu = null;
        _reset = null;
    }

    public Control Build()
    {
        _search.TextChanged -= OnSearch;
        _search.TextChanged += OnSearch;
        var bar = Toolbar();
        Fill();
        var head = Head();
        var btns = Row(Btn("Open apps.json", () => PathUtil.OpenFolder(Path.GetDirectoryName(AppCatalog.UserFile)!), false, "document_edit"),
            Btn("Add app", x.AddApp, true, "add"));
        DockPanel.SetDock(btns, Dock.Right);
        head.Children.Add(btns);
        head.Children.Add(H1("Apps"));
        return x.S.ShadowedApps.Count == 0
            ? Page(head, bar, Card(_table.Free()))
            : Page(head, NowBuiltIn(), bar, Card(_table.Free()));
    }

    /// <summary>Your apps.json entries for tools that a later version supports itself: the built-in app is used, so each can be removed.</summary>
    private Border NowBuiltIn()
    {
        var rows = x.S.ShadowedApps.Select(d => new TableRow(
        [
            Txt(d.Name, TextSize.Body, true), Mono(d.Id, TextSize.Caption),
            DangerBtn("Remove", () => AppActions.RemoveApp(x, d), "delete").With(b => b.MinHeight = Sizes.CompactControl)
        ]));
        var body = new StackPanel
        {
            Spacing = Space.M,
            Children =
            {
                Txt("AI Stackprint now supports these tools itself, so the built-in apps are used and your entries in apps.json are not.", TextSize.Small, false, Muted),
                Table.Build([new Col("App", "1.4*"), new Col("Id in apps.json", "2*"), new Col("", "Auto")], rows)
            }
        };
        return Section("info", $"Now built in · {x.S.ShadowedApps.Count}", body);
    }

    private void OnSearch(object? s, TextChangedEventArgs e)
    {
        if (s == _search && (_search.Text ?? "") != _q)
        {
            _q = _search.Text ?? "";
            Fill();
        }
    }   // the text set from _q is no change; an old box says nothing

    private void Fill()
    {
        var cats = x.ShownApps.Select(a => a.Category).Distinct().Order().ToList();
        KeepOffered(_offCats, cats);
        var found = x.ShownApps.Where(a => _q == "" || a.Name.Contains(_q, StringComparison.OrdinalIgnoreCase)).ToList();
        var inst = found.Count(a => a.Installed);
        var statuses = new List<string>();
        if (inst > 0)
        {
            statuses.Add("Installed");
        }

        if (inst < found.Count)
        {
            statuses.Add("Not found");
        }

        KeepOffered(_offStatus, statuses);
        var byStatus = found.Where(a => !_offStatus.Contains(a.Installed ? "Installed" : "Not found")).ToList();

        // Counts follow the search (and status for categories); a category with no apps is not listed
        UpdateToolbar(
            statuses.Select(s => (s, s, s == "Installed" ? inst : found.Count - inst)).ToList(),
            cats.Select(c => (Plural(c), c, byStatus.Count(a => a.Category == c))).ToList());

        var apps = byStatus.Where(a => !_offCats.Contains(a.Category)).OrderByDescending(a => a.Installed).ThenBy(a => a.Name).ToList();
        // A count column that is empty for every app is left out: less to read, and the rest fit. Decided over all apps, not the filtered ones, so the columns stay put while filtering
        var counts = Counts().Where(c => x.ShownApps.Any(a => c.Of(a) > 0)).ToList();
        var cols = new[] { Cols.Name("App"), Cols.Type("Category"), Cols.Status() }.Concat(counts.Select(c => Cols.Count(c.Head))).ToArray();
        var table = Table.Scrollable(Table.Build(cols, Rows(apps, counts, cols.Length), "No apps match. Clear the search or filters."), Table.MinWidth(cols));
        // Expand all / Collapse all: icon buttons over the top right of the table, while it is grouped
        var all = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = Space.XS,
            IsVisible = _group.Count > 0
        };
        all.Children.Add(IconBtn("chevron_down", "Expand all", () =>
        {
            _collapsed.Clear();
            Fill();
        }));
        all.Children.Add(IconBtn("chevron_up", "Collapse all", () =>
        {
            _collapsed.UnionWith(Keys(x.ShownApps));
            Fill();
        }));
        _table.Content = new StackPanel
        {
            Spacing = Space.XS,
            Children = { all, table }
        };
    }

    /// <summary>The count columns: their heading and how many of that kind an app has.</summary>
    private List<(string Head, Func<Models.AppInfo, int> Of)> Counts() =>
    [
        ("Plugins", a => x.Count(a.Id, "Plugin")), ("Skills", a => x.Count(a.Id, "Skill")), ("Commands", a => x.Count(a.Id, "Command")), ("Agents", a => x.Count(a.Id, "Agent")),
        ("Servers", a => x.S.Mcp.Count(m => m.Apps.Contains(a.Id))), ("Hooks", a => x.S.Hooks.Count(h => h.Apps.Contains(a.Id))), ("Config files", a => x.Count(a.Id, "Config"))
    ];

    private IEnumerable<TableRow> Rows(List<Models.AppInfo> apps, List<(string Head, Func<Models.AppInfo, int> Of)> counts, int cols)
    {
        TableRow Row(Models.AppInfo a) => new(
        [
            AppName(a.Logo, a.Category, a.Name).With(n => { if (!a.Installed) { n.Opacity = 0.6; } }), Txt(Cap(a.Category), TextSize.Small, false, Muted),
            a.Installed ? Status("Installed") : Txt("Not found", TextSize.Small, false, Muted),
            .. counts.Select(c => Shown(c.Of(a), a.Installed))
        ], () => x.Go("App:" + a.Id));
        if (_group.Count == 0)
        {
            foreach (var a in apps) { yield return Row(a); }
            yield break;
        }
        foreach (var g in apps.GroupBy(Key).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var open = !_collapsed.Contains(g.Key);
            var key = g.Key;
            var first = g.First();
            var name = ByCat ? Plural(first.Category) + (ByStat ? " · " + StatusName(first) : "") : StatusName(first);
            var cells = new Control[cols];
            cells[0] = W.Row(Icon(open ? "chevron_down" : "chevron_right", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Muted),
                ByCat ? Icon(Icons.ForCategory(first.Category), IconSize.S) : new Panel(), Title(name, TextSize.Body),
                Txt(ByStat ? $"{g.Count()}" : $"{g.Count(a => a.Installed)}", TextSize.Small, false, Muted));   // how many are here, not how many the app could know
            Grid.SetColumnSpan(cells[0], cols);   // the group title runs across the table
            for (var c = 1; c < cols; c++)
            {
                cells[c] = new Panel();
            }

            yield return new TableRow(cells, () =>
            {
                if (!_collapsed.Remove(key)) { _collapsed.Add(key); }
                Fill();
            });
            if (open)
            {
                foreach (var a in g)
                {
                    yield return Row(a);
                }
            }
        }
    }

    private static string StatusName(Models.AppInfo a) => a.Installed ? "Installed" : "Not found";
    private string Key(Models.AppInfo a) => (ByCat ? a.Category : "") + "|" + (ByStat ? (a.Installed ? "0" : "1") : "");
    private IEnumerable<string> Keys(IEnumerable<Models.AppInfo> apps) => apps.Select(Key).Distinct();

    private static TextBlock Shown(int n, bool installed) => Txt(n == 0 && !installed ? "-" : n.ToString(), TextSize.Body, false, n == 0 ? Muted : Text);   // a zero is quiet, so the numbers that are there stand out

    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Category as a filter label: "Agents", "Editors"; names without a plural stay as they are.</summary>
    private static string Plural(string c) => c switch { "shared" or "custom" => Cap(c), "chat" => "Chat apps", _ => Cap(c) + "s" };
}
