// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Servers page (route Mcp): every MCP server (tools for the model) and LSP server (code intelligence for the agent) in the apps' config files and
/// plugins, at every level (<see cref="McpServers"/>, read with each scan), highest level first. A search narrows the list; the pills pick a type,
/// or Warnings: servers that run an unpinned package or keep a key in plain text.
/// </summary>
internal class McpView(Ctx x)
{
    private string _query = "", _show = "";   // "" all, "MCP", "LSP", "w" warnings
    private ContentControl _table = new();
    private TextBox? _box;
    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() { _table = new(); _box = null; }

    public Control Build()
    {
        var all = x.S.Mcp;
        var head = new DockPanel();
        var pills = Pills([($"All {all.Count:N0}", ""), ($"MCP {all.Count(s => s.Kind == "MCP"):N0}", "MCP"), ($"LSP {all.Count(s => s.Kind == "LSP"):N0}", "LSP"),
            ($"Warnings {all.Count(s => s.Warn):N0}", "w")], _show, v => { _show = v; x.Refresh(); });
        DockPanel.SetDock(pills, Dock.Right);
        head.Children.Add(pills);
        head.Children.Add(H1("Servers"));
        var apps = all.SelectMany(s => s.Apps).Distinct().Count();
        var intro = Txt($"{ReadableLog.Plural(all.Count, "server")} in {ReadableLog.Plural(apps, "app")}. MCP servers give the model tools: programs the apps start, " +
            "or web services they connect to. LSP servers give the agent code intelligence (errors, definitions). Programs run with your rights. Secret-looking values are hidden. " +
            "Goose and Continue keep servers in YAML and are not read here (Goose extensions are on the Plugins page).", TextSize.Small, false, Muted);
        _box ??= SearchBox("Search name, app, command, plugin or file").With(b => b.Text = _query).OnPause(t => { _query = t.Trim(); Fill(); });
        Fill();
        return Page(head, intro, _box.Free(), _table.Free());
    }

    private void Fill()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = x.S.Mcp.Where(s => _show switch { "" => true, "w" => s.Warn, _ => s.Kind == _show })
            .Where(s => words.All(w => Text(s).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(AppName, StringComparer.OrdinalIgnoreCase).ThenBy(s => Levels.Rank(s.Level)).ToList();
        var rows = shown.Select(s => new TableRow(
        [
            Txt(s.Name, TextSize.Body, true),
            s.Apps.Count == 1 ? DetailPanel.AppLink(x, s.Apps[0], s.Apps[0]) : Txt(AppName(s), TextSize.Small),
            Level(s.Level, s.Project != null ? $"{s.Level} · {Ctx.ProjectName(s.Project)}" : s.Level, s.LevelLabel),
            Chip(s.Kind),
            Mono(s.What, TextSize.Caption),
            Txt(s.Note ?? "", TextSize.Caption, false, s.Warn ? WarningKey : Muted),
            SourceCell.For(x, s.Plugin, s.Apps.FirstOrDefault() ?? "", s.File),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(s.File))
        ]));
        _table.Content = Table.Build([Cols.Name(), Cols.App(), Cols.Level(), Cols.Type(),
            new Col("Runs", "2.4*"), new Col("Note", "1.5*"), new Col("Source", "2*"), Cols.Action()], rows,
            x.S.Mcp.Count == 0 ? "No MCP or LSP servers in the apps' config files or plugins." : "Nothing matches. Clear the search or choose All.");
    }

    private string AppName(McpServer s) => string.Join(", ", s.Apps.Select(a => x.S.Def(a)?.Name ?? a));

    private string Text(McpServer s) => $"{s.Name} {s.Kind} {AppName(s)} {s.What} {s.File} {s.Note} {s.Level} {s.Project} {s.Plugin}";
}
