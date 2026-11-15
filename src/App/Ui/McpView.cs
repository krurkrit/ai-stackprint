// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// MCP servers page: every server in the apps' config files at every level (<see cref="McpServers"/>, read with each scan), highest level first.
/// A search narrows the list; Warnings shows servers that run an unpinned package or keep a key in plain text.
/// </summary>
internal class McpView(Ctx x)
{
    private string _query = "";
    private bool _warnOnly;
    private ContentControl _table = new();
    private TextBox? _box;
    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() { _table = new(); _box = null; }

    public Control Build()
    {
        var all = x.S.Mcp;
        var warn = all.Count(s => s.Warn);
        var head = new DockPanel();
        var pills = Pills([($"All {all.Count:N0}", "a"), ($"Warnings {warn:N0}", "w")], _warnOnly ? "w" : "a", v => { _warnOnly = v == "w"; x.Refresh(); });
        DockPanel.SetDock(pills, Dock.Right);
        head.Children.Add(pills);
        head.Children.Add(H1("MCP servers"));
        var apps = all.SelectMany(s => s.Apps).Distinct().Count();
        var intro = Txt($"{ReadableLog.Plural(all.Count, "server")} in {ReadableLog.Plural(apps, "app")}: programs the apps start, or web services they connect to, for tools. " +
            "Programs run with your rights. Secret-looking values are hidden. Goose and Continue keep servers in YAML and are not read here (Goose extensions are on the Plugins page).",
            TextSize.Small, false, Muted);
        _box ??= SearchBox("Search name, app, command or file").With(b => b.Text = _query).OnPause(t => { _query = t.Trim(); Fill(); });
        Fill();
        return Page(head, intro, _box.Free(), _table.Free());
    }

    private void Fill()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = x.S.Mcp.Where(s => !_warnOnly || s.Warn).Where(s => words.All(w => Text(s).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => Levels.Rank(s.Level)).ThenBy(AppName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = shown.Select(s => new TableRow(
        [
            Txt(s.Name, TextSize.Body, true),
            s.Apps.Count == 1 ? DetailPanel.AppLink(x, s.Apps[0], s.Apps[0]) : Txt(AppName(s), TextSize.Small),
            Level(s.Level, s.Project != null ? $"{s.Level} · {Ctx.ProjectName(s.Project)}" : s.Level, s.LevelLabel),
            Mono(s.What, TextSize.Caption),
            Txt(s.Note ?? "", TextSize.Caption, false, s.Warn ? WarningKey : Muted),
            ItemBrowser.PathCell(s.File),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(s.File))
        ]));
        _table.Content = Table.Build([new Col("Name", "1.2*", Min: 96), new Col("App", "1.1*", Min: 96), new Col("Level", "1*", Min: 56), new Col("Runs", "2.6*"),
            new Col("Note", "1.6*"), new Col("File", "2*"), new Col("", "40")], rows,
            x.S.Mcp.Count == 0 ? "No MCP servers in the apps' config files." : "Nothing matches. Clear the search or choose All.");
    }

    private string AppName(McpServer s) => string.Join(", ", s.Apps.Select(a => x.S.Def(a)?.Name ?? a));

    private string Text(McpServer s) => $"{s.Name} {AppName(s)} {s.What} {s.File} {s.Note} {s.Level} {s.Project}";
}
