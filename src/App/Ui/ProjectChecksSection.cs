// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Documents;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Project page: checks of the project's AI files (<see cref="ProjectChecks"/>: rules that never load, text loaded twice, broken links,
/// secrets in config files, settings that skip prompts) and the MCP servers and hooks its config files start. Read in the background once per scan.
/// </summary>
internal class ProjectChecksSection(Ctx x)
{
    private string? _project;
    private object? _scan;
    private ProjectCheck? _last;
    private ContentControl _host = new();

    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() => _host = new();

    public Control Build(string project)
    {
        if (_project != project || !ReferenceEquals(_scan, x.S.Items))
        {
            _project = project;
            _scan = x.S.Items;
            _last = null;
        }
        if (_last != null)
        {
            _host.Content = Show(_last);
        }
        else
        {
            var defs = x.S.Defs.Where(d => x.ShownApps.Any(a => a.Id == d.Id)).ToList();
            Busy.Into(_host, "Checking AI files…", _ => ProjectChecks.Run(project, project, defs, PathUtil.Home), r =>
            {
                _last = r;
                return Show(r);
            });
        }

        return Section("warning", "Checks", _host.Free());
    }

    private StackPanel Show(ProjectCheck r)
    {
        var sp = new StackPanel { Spacing = Space.L };
        var issues = r.Issues.OrderByDescending(i => i.Finding.Warning).ThenBy(i => i.File, StringComparer.OrdinalIgnoreCase).ToList();
        var warn = issues.Count(i => i.Finding.Warning);
        sp.Children.Add(Txt(issues.Count == 0 ? "No problems found in this project's AI files. Context limits are checked above."
            : $"{ReadableLog.Plural(warn, "warning")}, {ReadableLog.Plural(issues.Count - warn, "suggestion")}. Context limits are checked above.", TextSize.Small, false, Muted));
        if (issues.Count > 0)
        {
            var rows = issues.Select(i => new TableRow(
            [
                Icon(i.Finding.Warning ? "warning" : "info", IconSize.S).Themed(TextElement.ForegroundProperty, i.Finding.Warning ? WarningKey : Muted),
                Txt(i.Finding.Text, TextSize.Small), Txt(string.Join(", ", i.Apps.Select(a => x.S.Def(a)?.Name ?? a)), TextSize.Small, false, Muted),
                i.File is { } f ? IconBtn("open", "Open file", () => PathUtil.OpenFile(f)) : new Panel()
            ]));
            sp.Children.Add(Table.Build([new Col("", "24"), new Col("Finding", "5*"), Cols.App("Apps"), Cols.Action()], rows));
        }

        if (r.Ignores.Count > 0)
        {
            sp.Children.Add(Title($"Ignore files · {r.Ignores.Count}", TextSize.Subtitle));
            sp.Children.Add(Txt("Files an app's ignore file keeps out of its view of this project.", TextSize.Small, false, Muted));
            var ignored = r.Ignores.Select(i => new TableRow(
            [
                Mono(ProjectChecks.Rel(_project!, i.File), TextSize.Small), Txt(string.Join(", ", i.Apps.Select(a => x.S.Def(a)?.Name ?? a)), TextSize.Small, false, Muted),
                Txt($"{i.Hidden:N0} of {i.Total:N0} files", TextSize.Small), IconBtn("open", "Open file", () => PathUtil.OpenFile(i.File))
            ]));
            sp.Children.Add(Table.Build([Cols.Name("File"), Cols.App(), new Col("Hides", "1.4*"), Cols.Action()], ignored));
        }

        sp.Children.Add(Title($"MCP servers and hooks · {r.Programs.Count}", TextSize.Subtitle));
        sp.Children.Add(Txt("Programs this project's config files start when an app uses them. They run with your rights; secret-looking values are hidden.", TextSize.Small, false, Muted));
        var programs = r.Programs.Select(p => new TableRow(
        [
            Txt(p.Name, TextSize.Small, true), Txt(p.Kind, TextSize.Small), Mono(p.What, TextSize.Caption), Mono(ProjectChecks.Rel(_project!, p.File), TextSize.Caption),
            Txt(p.Note ?? "", TextSize.Caption, false, p.Note?.StartsWith("Not pinned", StringComparison.Ordinal) == true ? WarningKey : Muted),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(p.File))
        ]));
        sp.Children.Add(Table.Build([Cols.Name("Name", "1.2*"), Cols.Type(), new Col("Runs", "3*"), new Col("File", "1.4*"), new Col("Note", "1.6*"), Cols.Action()],
            programs, "No MCP servers or hooks in this project's config files."));
        return sp;
    }
}
