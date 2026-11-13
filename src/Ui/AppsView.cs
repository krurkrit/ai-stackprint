using Avalonia;
using Avalonia.Controls;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Flat list of apps with search, status and category filters, grouped by category with collapsible headings (kept while the app runs).
/// Clicking a row opens the app detail page.
/// </summary>
class AppsView(Ctx x)
{
    readonly HashSet<string> _offCats = new(), _offStatus = new(), _collapsed = new();   // unchecked categories and statuses; collapsed headings
    string _q = "";
    bool _byCat = true, _byStatus;                                  // Group by check boxes
    readonly TextBox _search = SearchBox("Search apps");
    readonly ContentControl _table = new(), _filters = new();

    public Control Build()
    {
        _search.TextChanged -= OnSearch; _search.TextChanged += OnSearch;
        Fill();
        var head = new DockPanel();
        var btns = Row(Btn("Open apps.json", () => PathUtil.OpenFolder(Path.GetDirectoryName(AppCatalog.UserFile)!), false, "document_edit"),
            Btn("Add app", x.AddApp, true, "add"));
        DockPanel.SetDock(btns, Dock.Right);
        head.Children.Add(btns); head.Children.Add(H1("AI apps"));
        return Page(head, _search.Free(), _filters.Free(), Card(_table.Free()));
    }

    void OnSearch(object? s, TextChangedEventArgs e) { _q = _search.Text ?? ""; Fill(); }

    void Fill()
    {
        var cats = x.ShownApps.Select(a => a.Category).Distinct().Order().ToList();
        KeepOffered(_offCats, cats);
        var found = x.ShownApps.Where(a => _q == "" || a.Name.Contains(_q, StringComparison.OrdinalIgnoreCase)).ToList();
        int inst = found.Count(a => a.Installed);
        var statuses = new List<string>();
        if (inst > 0) statuses.Add("Installed");
        if (inst < found.Count) statuses.Add("Not found");
        KeepOffered(_offStatus, statuses);
        var byStatus = found.Where(a => !_offStatus.Contains(a.Installed ? "Installed" : "Not found")).ToList();

        // Counts follow the search (and status for categories); a category with no apps is not listed
        var f = new StackPanel { Spacing = 10 };
        if (statuses.Count > 1)
            f.Children.Add(FilterRow("Status", Checks(new (Control, string)[] { (Counted(Txt("Installed", 14), inst), "Installed"), (Counted(Txt("Not found", 14), found.Count - inst), "Not found") }, _offStatus, Fill)));
        if (cats.Count > 1)
            f.Children.Add(FilterRow("Category", Checks(cats.Select(c => (Counted(Txt(Plural(c), 14), byStatus.Count(a => a.Category == c)), c)).ToList(), _offCats, Fill)));
        var groupRow = new List<Control> { Check("Category", _byCat, v => { _byCat = v; Fill(); }), Check("Status", _byStatus, v => { _byStatus = v; Fill(); }) };
        if (_byCat || _byStatus)
        {
            groupRow.Add(Btn("Expand all", () => { _collapsed.Clear(); Fill(); }, false, "chevron_down").With(b => b.MinHeight = 32));
            groupRow.Add(Btn("Collapse all", () => { _collapsed.UnionWith(Keys(x.ShownApps)); Fill(); }, false, "chevron_right").With(b => b.MinHeight = 32));
        }
        f.Children.Add(FilterRow("Group by", groupRow.ToArray()));
        _filters.Content = f;

        var apps = byStatus.Where(a => !_offCats.Contains(a.Category)).OrderByDescending(a => a.Installed).ThenBy(a => a.Name).ToList();
        var cols = new[] { new Col("App", "2*"), new Col("Category", "1*"), new Col("Status", "1*"), new Col("Plugins", ".6*", true), new Col("Skills", ".6*", true), new Col("Config files", ".8*", true) };
        _table.Content = Table.Build(cols, Rows(apps, cols.Length), "No apps match. Clear the search or filters.");
    }

    IEnumerable<TableRow> Rows(List<Models.AppInfo> apps, int cols)
    {
        TableRow Row(Models.AppInfo a) => new(new Control[]
        {
            AppName(a.Logo, a.Category, a.Name).With(n => { if (!a.Installed) n.Opacity = 0.6; }), Txt(Cap(a.Category), 13, false, Muted),
            a.Installed ? Status("Installed") : Txt("Not found", 13, false, Muted),
            Count(a.Id, "Plugin", a.Installed), Count(a.Id, "Skill", a.Installed), Count(a.Id, "Config", a.Installed)
        }, () => x.Go("App:" + a.Id));
        if (!_byCat && !_byStatus) { foreach (var a in apps) yield return Row(a); yield break; }
        foreach (var g in apps.GroupBy(Key).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            bool open = !_collapsed.Contains(g.Key); string key = g.Key;
            var first = g.First();
            string name = _byCat ? Plural(first.Category) + (_byStatus ? " · " + StatusName(first) : "") : StatusName(first);
            var cells = new Control[cols];
            cells[0] = W.Row(Icon(open ? "chevron_down" : "chevron_right", 14).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Muted),
                _byCat ? Icon(Icons.ForCategory(first.Category), 16) : new Panel(), Title(name, 14),
                Txt(_byStatus ? $"{g.Count()}" : $"{g.Count(a => a.Installed)} of {g.Count()} installed", 13, false, Muted));
            Grid.SetColumnSpan(cells[0], cols);   // the group title runs across the table
            for (int c = 1; c < cols; c++) cells[c] = new Panel();
            yield return new TableRow(cells, () => { if (!_collapsed.Remove(key)) _collapsed.Add(key); Fill(); });
            if (open) foreach (var a in g) yield return Row(a);
        }
    }

    static string StatusName(Models.AppInfo a) => a.Installed ? "Installed" : "Not found";
    string Key(Models.AppInfo a) => (_byCat ? a.Category : "") + "|" + (_byStatus ? (a.Installed ? "0" : "1") : "");
    IEnumerable<string> Keys(IEnumerable<Models.AppInfo> apps) => apps.Select(Key).Distinct();

    Control Count(string id, string kind, bool installed)
    {
        int n = x.Count(id, kind);
        return Txt(n == 0 && !installed ? "-" : n.ToString());
    }

    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Category as a filter label: "Agents", "Editors"; names without a plural stay as they are.</summary>
    static string Plural(string c) => c switch { "shared" or "custom" => Cap(c), "chat" => "Chat apps", _ => Cap(c) + "s" };
}
