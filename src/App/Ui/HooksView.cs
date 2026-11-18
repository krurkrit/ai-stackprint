// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Hooks page: every hook (a command or prompt an app runs on an event) in the apps' settings and hook files and in plugins, at every level
/// (<see cref="Hooks"/>, read with each scan), highest level first. A search narrows the list; Warnings shows hooks that run a missing script
/// or download and run code in one step.
/// </summary>
internal class HooksView(Ctx x)
{
    private string _query = "", _show = "";   // "" all, "w" warnings
    private ContentControl _table = new();
    private TextBox? _box;
    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() { _table = new(); _box = null; }

    public Control Build()
    {
        var all = x.S.Hooks;
        var head = Head();
        var pills = Pills([($"All {all.Count:N0}", ""), ($"Warnings {all.Count(h => h.Warn):N0}", "w")], _show, v => { _show = v; x.Refresh(); });
        DockPanel.SetDock(pills, Dock.Right);
        head.Children.Add(pills);
        head.Children.Add(H1("Hooks"));
        var apps = all.SelectMany(h => h.Apps).Distinct().Count();
        var intro = Txt($"{ReadableLog.Plural(all.Count, "hook")} in {ReadableLog.Plural(apps, "app")}. A hook runs a command (with your rights) or asks the model a prompt " +
            "when something happens: a tool is used, a prompt is sent, a session starts or ends. The matcher says which tools or files it applies to. " +
            "Secret-looking values are hidden. Kiro's hook files are read best effort.", TextSize.Small, false, Muted);
        _box ??= SearchBox("Search event, matcher, app, command, plugin or file").With(b => b.Text = _query).OnPause(t => { _query = t.Trim(); Fill(); });
        Fill();
        return Page(head, intro, _box.Free(), _table.Free());
    }

    private void Fill()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = x.S.Hooks.Where(h => _show != "w" || h.Warn)
            .Where(h => words.All(w => Text(h).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(h => h.Event, StringComparer.OrdinalIgnoreCase).ThenBy(AppName, StringComparer.OrdinalIgnoreCase).ThenBy(h => Levels.Rank(h.Level)).ToList();
        var rows = shown.Select(h => new TableRow(
        [
            Txt(h.Event, TextSize.Body, true),
            h.Apps.Count == 1 ? DetailPanel.AppLink(x, h.Apps[0], h.Apps[0]) : Txt(AppName(h), TextSize.Small),
            Level(h.Level, h.Project != null ? $"{h.Level} · {Ctx.ProjectName(h.Project)}" : h.Level, h.LevelLabel),
            h.Matcher is { Length: > 0 } m && m != "*" ? Mono(m, TextSize.Caption) : Txt("Every", TextSize.Caption, false, Muted),
            (h.Type == "command" ? Mono(h.What, TextSize.Caption) : Txt(h.What, TextSize.Caption)).With(t => { t.MaxLines = 3; t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; ToolTip.SetTip(t, t.Text); }),
            Txt(h.Note ?? "", TextSize.Caption, false, h.Warn ? WarningKey : Muted),
            SourceCell.For(x, h.Plugin, h.Apps.FirstOrDefault() ?? "", h.File),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(h.File))
        ]));
        _table.Content = TableSurface(Table.Build([Cols.Name("Event"), Cols.App(), Cols.Level(), Cols.Type("Matcher"),
            new Col("Runs", "2.4*"), new Col("Note", "1.5*"), new Col("Source", "2*"), Cols.Action()], rows,
            x.S.Hooks.Count == 0 ? "No hooks in the apps' settings or hook files or plugins." : "Nothing matches. Clear the search or choose All."));
    }

    private string AppName(Hook h) => string.Join(", ", h.Apps.Select(a => x.S.Def(a)?.Name ?? a));

    private string Text(Hook h) => $"{h.Event} {h.Matcher} {AppName(h)} {h.What} {h.File} {h.Note} {h.Level} {h.Project} {h.Plugin}";
}
