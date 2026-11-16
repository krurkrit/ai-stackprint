// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Everything that applies inside one project folder: what each app loads into context there (with limits), checks of its AI files, the instruction files,
/// and its own skills plus user- and system-level ones in the same list as the Skills page (filters, bottom panel).
/// </summary>
internal class ProjectView(Ctx x, Action<string> removeProject, Action<string> relocateProject)
{
    private static readonly string[] _order = Levels.Order;
    private readonly ItemBrowser _browser = new(x, "Skill");
    private readonly ContextSection _context = new(x);
    private readonly ProjectChecksSection _checks = new(x);
    private readonly ClaudeSettingsSection _claude = new(x);
    private string? _project;   // project the list state belongs to: reset when another opens

    /// <summary>Rows that take effect in <paramref name="project"/>: rows from that project, or rows not tied to any project.</summary>
    private static bool Applies(Item i, string project) => i.Project == project || (i.Project == null && !Scanner.InProject(i.Level));

    /// <summary>The page was left: see <see cref="ItemBrowser.Leave"/>.</summary>
    public void Leave() { _browser.Leave(); _context.Leave(); _checks.Leave(); }

    public Control Build(string project)
    {
        if (_project != project) { _project = project; _browser.Reset(); }
        if (!Directory.Exists(project))
        {
            return Missing(project);   // reached from search or Back after the folder was moved or deleted
        }

        var all = x.S.Items.Where(i => Applies(i, project)).ToList();
        var skills = all.Where(i => i.Kind == "Skill").ToList();

        var actions = Row(Btn("Add skill", () => AppActions.AddSkillPickApp(x, project), true, "add"),
            Btn("Open folder", () => PathUtil.OpenFolder(project), false, "folder_open"),
            DangerBtn("Remove from list", () => removeProject(project), "dismiss"));
        actions.VerticalAlignment = VerticalAlignment.Top;
        var title = new StackPanel { Spacing = Space.S, Children = { H1("Projects", null, Ctx.ProjectName(project)), Mono(project) } };
        var head = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Row(IconTile("folder", Sizes.LogoXL), title));

        var summary = new WrapPanel { Children = { Levels.InfoButton().With(b => b.Margin = new Avalonia.Thickness(0, 0, 4, 0)) } };
        foreach (var l in _order)
        {
            summary.Children.Add(Chip($"{l} skills {skills.Count(s => s.Level == l)}", LevelKey(l)).With(c => c.Margin = new Avalonia.Thickness(0, 0, 8, 0)));
        }

        var top = new List<Control> { head, summary, _context.Build(project), _checks.Build(project), _claude.Build(project), AiFoldersSection.Build(x, project, all), Configs(all), Title($"Skills · {skills.Count}", TextSize.Title).With(t => t.Margin = new Avalonia.Thickness(0, 8, 0, 0)) };
        return _browser.Build(top, skills);
    }

    private Control Missing(string project)
    {
        var text = new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt($"This folder no longer exists: {project}. If the project moved, choose its new location; it keeps its place in the list.", TextSize.Body),
                Row(Btn("Choose new location", () => relocateProject(project), true, "folder_open"), DangerBtn("Remove from list", () => removeProject(project), "delete"))
            }
        };
        var title = new StackPanel { Spacing = Space.S, Children = { H1("Projects", null, Ctx.ProjectName(project)), Mono(project) } };
        return Page(Row(IconTile("folder", Sizes.LogoXL), title), Section("warning", "Folder not found", text));
    }

    /// <summary>Instruction/settings files read in this project, one row per file with every app that reads it.</summary>
    private Border Configs(List<Item> all)
    {
        var rows = all.Where(i => i.Kind == "Config").GroupBy(i => i.Path)
            .OrderBy(g => Array.IndexOf(_order, g.First().Level)).ThenBy(g => g.First().Name)
            .Select(g =>
            {
                var i = g.First();
                return new TableRow(
                [
                    Txt(i.Name, TextSize.Body, true), Txt(string.Join(", ", g.Select(c => c.Source).Distinct()), TextSize.Small), Txt(i.Description, TextSize.Small), HoldsCell.For(x, i),
                    AppDetailView.LevelCell(i), Mono(i.Path, TextSize.Caption),
                    IconBtn("open", "Open file", () => PathUtil.OpenFile(i.Path))
                ]);
            });
        var cols = new[] { new Col("File", "1.2*"), new Col("Read by", "1.4*"), new Col("Type", "1*"), new Col("Holds", "1.6*"), new Col("Level", "1.2*"), new Col("Path", "2.2*"), new Col("", "40") };
        return Section("document", "Instructions & settings", Table.Build(cols, rows, "No instruction or settings files apply."));
    }
}
