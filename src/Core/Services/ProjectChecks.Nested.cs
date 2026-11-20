// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Context limits in every folder a session may start in: which instruction files load depends on the start folder (Codex reads one AGENTS.md
/// per folder from the repo root down to it, and stops at project_doc_max_bytes), so a chain over the limit in packages/api shows only when a
/// session starts there. Each folder below the project that holds an instruction file is checked as a start folder.
/// </summary>
public static partial class ProjectChecks
{
    /// <summary>At most this many start folders are checked (the first ones in name order), so a large monorepo stays quick.</summary>
    internal const int MaxStartFolders = 50;
    private const int StartDepth = 3;

    /// <summary>Folders below the project that hold an instruction file (AGENTS.md, CLAUDE.md, GEMINI.md …), in name order; at most <see cref="MaxStartFolders"/>.</summary>
    public static List<string> StartFolders(string project, IReadOnlyList<AppDefinition> defs)
    {
        var names = defs.SelectMany(d => d.Configs)
            .Where(c => c.Kind == "instructions" && Scanner.InProject(c.Level) && c.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !c.Path.Contains('/') && !c.Path.Contains('\\'))
            .Select(c => c.Path).Append("AGENTS.override.md").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return [.. ContextBudget.Below(project, names, StartDepth).Select(f => Path.GetDirectoryName(f)!).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxStartFolders)];
    }

    /// <summary>
    /// The findings each app has when a session starts in a sub-folder and not in the project folder: a file cut or not loaded, or a total over
    /// the limit. The same finding is reported once, for the first folder that has it.
    /// </summary>
    private static void Nested(string project, List<(AppDefinition App, ContextReport Report)> context, IReadOnlyList<AppDefinition> defs, string home, List<ProjectIssue> issues)
    {
        var seen = context.SelectMany(c => c.Report.Findings.Select(f => (c.App.Id, f.Text))).ToHashSet();
        var folders = StartFolders(project, defs);
        // Every folder is read for every app (files only, nothing shared), so the folders are read side by side; "reported once, for the first folder that has it" is decided after, in order
        var reads = folders.AsParallel().AsOrdered().Select(folder => context.Select(c => ContextBudget.Find(c.App.Context)!.Read(project, folder, home)).ToList()).ToList();
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var rel = Path.GetRelativePath(project, folder).Replace('\\', '/');
            for (var a = 0; a < context.Count; a++)
            {
                var app = context[a].App;
                var r = reads[i][a];
                foreach (var f in r.Findings.Where(f => seen.Add((app.Id, f.Text))))
                {
                    issues.Add(new([app.Id], folder, new($"When {app.Name} starts in {rel}: {f.Text}", f.Warning)));
                }
            }
        }
    }
}
