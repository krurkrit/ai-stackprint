// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Servers page (route Mcp): every MCP server (tools for the model) and LSP server (code intelligence for the agent) in the apps' config files and
/// plugins, at every level (<see cref="McpServers"/>, read with each scan), highest level first. The tool bar (<see cref="FilterBar{T}"/>) has the search
/// and App, Level, Type and Status menus; Status "Warning": servers that run an unpinned package or keep a key in plain text.
/// </summary>
internal class McpView(Ctx x)
{
    private string _query = "";
    private readonly HashSet<string> _offApps = [], _offLevels = [], _offTypes = [], _offStatus = [];
    private FilterBar<McpServer>? _bar;
    private ContentControl _table = new();
    private TextBox? _box;
    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave()
    {
        _table = new();
        _box = null;
        _bar?.Leave();
    }

    private FilterBar<McpServer> Bar()
    {
        if (_bar != null)
        {
            return _bar;
        }

        _bar = new FilterBar<McpServer>(() => _query, Fill, () =>
        {
            _query = "";
            _box?.Text = "";
        });
        _bar.Add("App", _offApps, s => s.Apps, x.AppName, vs => vs.OrderBy(x.AppName, StringComparer.OrdinalIgnoreCase));
        _bar.Add("Level", _offLevels, s => [s.Level], order: vs => Levels.Order.Where(vs.Contains));
        _bar.Add("Type", _offTypes, s => [s.Kind]);
        _bar.Add("Status", _offStatus, s => [s.Warn ? "Warning" : "OK"], order: vs => vs.OrderByDescending(v => v == "OK"));
        return _bar;
    }

    public Control Build()
    {
        var all = x.S.Mcp;
        var head = Head();
        head.Children.Add(H1("Servers"));
        var apps = all.SelectMany(s => s.Apps).Distinct().Count();
        var intro = Txt($"{ReadableLog.Plural(all.Count, "server")} in {ReadableLog.Plural(apps, "app")}. MCP servers give the model tools: programs the apps start, " +
            "or web services they connect to. LSP servers give the agent code intelligence (errors, definitions). Programs run with your rights. Secret-looking values are hidden. " +
            "Goose and Continue keep servers in YAML and are not read here (Goose extensions are on the Plugins page).", TextSize.Small, false, Muted);
        _box ??= SearchBox("Search name, app, command, plugin or file").With(b => b.Text = _query).OnPause(t =>
        {
            _query = t.Trim();
            Fill();
        });
        var bar = Bar().Build(_box);
        Fill();
        return Page(head, intro, bar, _table.Free());
    }

    private void Fill()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var searched = x.S.Mcp.Where(s => words.All(w => Text(s).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        var shown = Bar().Apply(searched, x.S.Mcp)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(AppName, StringComparer.OrdinalIgnoreCase).ThenBy(s => Levels.Rank(s.Level)).ToList();
        var rows = shown.Select(s => new TableRow(
        [
            Txt(s.Name, TextSize.Body, true),
            s.Apps.Count == 1 ? DetailPanel.AppLink(x, s.Apps[0], s.Apps[0]) : Txt(AppName(s), TextSize.Small),
            Level(s.Level, s.Project != null ? $"{s.Level} · {Ctx.ProjectName(s.Project)}" : s.Level, s.LevelLabel),
            Chip(s.Kind),
            Mono(s.What, TextSize.Caption).With(t =>
            {
                t.MaxLines = 3;
                t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(t, t.Text);
            }),   // a long command shows 3 lines; the whole command is in the tooltip and copies with a right-click
            Txt(s.Note ?? "", TextSize.Caption, false, s.Warn ? WarningKey : Muted),
            SourceCell.For(x, s.Plugin, s.Apps.FirstOrDefault() ?? "", s.File),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(s.File))
        ]));
        _table.Content = TableSurface(Table.Build([Cols.Name(), Cols.App(), Cols.Level(), Cols.Type(),
            new Col("Runs", "2.4*"), new Col("Note", "1.5*"), new Col("Source", "2*"), Cols.Action()], rows,
            x.S.Mcp.Count == 0 ? "No MCP or LSP servers in the apps' config files or plugins." : "Nothing matches. Clear the search or filters."));
    }

    private string AppName(McpServer s) => string.Join(", ", s.Apps.Select(x.AppName));

    private string Text(McpServer s) => $"{s.Name} {s.Kind} {AppName(s)} {s.What} {s.File} {s.Note} {s.Level} {s.Project} {s.Plugin}";
}
