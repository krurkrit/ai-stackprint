// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Documents;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Checks of the last scan (see <see cref="Health"/>, <see cref="ProjectChecks"/>): warnings first, then guidelines; a row opens the skill, plugin or project.</summary>
internal class HealthView(Ctx x)
{
    private bool _all;   // also show guidelines (information), not only warnings

    public Control Build()
    {
        var issues = x.S.Health;
        int warn = issues.Count(i => i.Finding.Warning), info = issues.Count - warn;
        var head = new DockPanel();
        var pills = Pills([($"Warnings {warn:N0}", "w"), ($"All {issues.Count:N0}", "a")], _all ? "a" : "w", v => { _all = v == "a"; x.Refresh(); });
        DockPanel.SetDock(pills, Dock.Right);
        head.Children.Add(pills);
        head.Children.Add(H1("Health"));
        var intro = Txt("Skills checked against the Agent Skills format (agentskills.io), Claude Code's skill-list budget, duplicate names, and plugins that are missing or run programs. " +
            "Projects: instruction files over an app's limit, rules that never load, text loaded twice, broken links, secrets and permission settings in config files. Details on each project page.", TextSize.Small, false, Muted);

        var page = new List<Control> { head, intro };
        var shown = issues.Where(i => _all || i.Finding.Warning).ToList();
        if (shown.Count == 0)
        {
            page.Add(Card(Row(Icon("info", IconSize.M).Themed(TextElement.ForegroundProperty, SuccessKey), Txt(issues.Count == 0 ? "Nothing to report." : $"No warnings. {info:N0} guidelines: choose All to see them.", TextSize.Body))));
        }

        foreach (var g in shown.GroupBy(i => i.Area))
        {
            var rows = g.OrderByDescending(i => i.Finding.Warning).ThenBy(i => i.Subject, StringComparer.OrdinalIgnoreCase).Select(i => new TableRow(
            [
                Icon(i.Finding.Warning ? "warning" : "info", IconSize.S).Themed(TextElement.ForegroundProperty, i.Finding.Warning ? WarningKey : Muted),
                Txt(i.Subject, TextSize.Body, true), i.App == "" ? new Panel() : DetailPanel.AppLink(x, i.App, i.App), Txt(i.Finding.Text, TextSize.Small)
            ], i.Item is { } it ? () => x.Open(it) : i.Project is { } p ? () => x.Go("Project:" + p) : null));
            page.Add(Section(g.Key switch { "Skills" => "sparkle", "Projects" => "folder", _ => "puzzle_piece" }, $"{g.Key} · {g.Count():N0}",
                Table.Build([new Col("", "24"), new Col("Name", "1.4*"), new Col("App", "1.2*"), new Col("Finding", "4*")], rows)));
        }
        return Page([.. page]);
    }
}
