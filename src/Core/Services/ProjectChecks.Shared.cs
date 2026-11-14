// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

public static partial class ProjectChecks
{
    /// <summary>Apps that name their instruction file differently: the file, and how it can share AGENTS.md.</summary>
    private static readonly (string File, string App, string How)[] _ownFiles =
    [
        ("CLAUDE.md", "Claude Code", "put the shared rules in AGENTS.md and add a line @AGENTS.md to CLAUDE.md"),
        ("GEMINI.md", "Gemini CLI", "add a line @AGENTS.md to GEMINI.md, or set context.fileName in .gemini/settings.json"),
        ("QWEN.md", "Qwen Code", "add a line @AGENTS.md to QWEN.md, or set context.fileName in .qwen/settings.json")
    ];

    /// <summary>Info: an app's own file sits next to AGENTS.md without pointing to it, so the two drift apart; apps that read no file here while others do.</summary>
    private static void Shared(string project, List<(AppDefinition App, ContextReport Report)> context, IReadOnlyList<AppDefinition> defs, List<ProjectIssue> issues)
    {
        var agents = Path.Combine(project, "AGENTS.md");
        if (File.Exists(agents))
        {
            foreach (var (name, app, how) in _ownFiles)
            {
                var own = Path.Combine(project, name);
                if (File.Exists(own) && !ContextBudget.ReadText(own).Contains("AGENTS.md", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new([], own, new($"{name} and AGENTS.md both exist, and {name} does not point to AGENTS.md: {app} and the apps that read AGENTS.md may get different rules. Suggestion: {how}.", false)));
                }
            }
        }

        var reading = context.Count(c => c.Report.Files.Any(f => ContextBudget.Inside(f.Path, project)));
        if (reading == 0)
        {
            return;
        }

        var idle = context.Where(c => !c.Report.Files.Any(f => ContextBudget.Inside(f.Path, project)))
            .Select(c => (c.App, Files: defs.First(d => d.Id == c.App.Id).Configs.Where(f => f.Kind == "instructions" && f.Level == "Project").Select(f => f.Path).Distinct().Take(2).ToList()))
            .Where(c => c.Files.Count > 0).ToList();
        if (idle.Count > 0)
        {
            issues.Add(new([.. idle.Select(i => i.App.Id)], null, new("No instruction file here for " + string.Join(", ", idle.Take(4).Select(i => $"{i.App.Name} (reads {string.Join(" or ", i.Files)})")) + (idle.Count > 4 ? $" and {idle.Count - 4} more" : "") + ". Other apps read files in this project.", false)));
        }
    }
}
