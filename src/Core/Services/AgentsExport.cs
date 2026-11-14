// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// Builds an AGENTS.md from the instruction files the apps load in a project: one "From file" section per file (front matter removed, "Applies to" for rules that load
/// only for matching files), lines already written dropped. Skips personal files (*.local.*), files outside the project and AGENTS.md itself. Writes nothing.
/// </summary>
public static partial class AgentsExport
{
    private const int MinLine = 12;
    private static readonly LoadWhen[] _loads = [LoadWhen.Always, LoadWhen.Imported, LoadWhen.Conditional, LoadWhen.AgentDecides];

    /// <summary>The source files in the order they are written, and the text; null text when there is nothing to export.</summary>
    public static (List<string> Sources, string? Text) Build(string project, IEnumerable<ContextReport> reports, DateTime today)
    {
        var files = reports.SelectMany(r => r.Files).Where(f => _loads.Contains(f.Load) && ContextBudget.Inside(f.Path, project) && File.Exists(f.Path)
                && !Path.GetFileName(f.Path).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f.Path).Contains(".local.", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => (g.First().Path, Patterns: g.SelectMany(f => f.Patterns ?? []).Distinct().ToList()))
            .OrderBy(f => ContextBudget.Same(Path.GetDirectoryName(f.Path) ?? "", project) ? 0 : 1).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new HashSet<string>();
        var body = new StringBuilder();
        var used = new List<string>();
        foreach (var (path, patterns) in files)
        {
            var text = Clean(SkillDoc.Parse(ContextBudget.ReadText(path)).Body, seen);
            if (text.Trim().Length == 0)
            {
                continue;
            }

            var rel = Path.GetRelativePath(project, path).Replace('\\', '/');
            used.Add(rel);
            body.Append("## From ").Append(rel).Append("\n\n");
            if (patterns.Count > 0)
            {
                body.Append("Applies to: ").Append(string.Join(", ", patterns)).Append("\n\n");
            }

            body.Append(text.Trim()).Append("\n\n");
        }

        if (used.Count == 0)
        {
            return (used, null);
        }

        var head = $"# AGENTS.md\n\n<!-- Exported by AI Stackprint on {today:yyyy-MM-dd} from {string.Join(", ", used)}. Edit those files, then export again. -->\n\n";
        return (used, head + body.ToString().TrimEnd() + "\n");
    }

    /// <summary>Drops lines already written (12+ characters), @AGENTS.md imports and heading-less duplicates; keeps blank lines tidy.</summary>
    private static string Clean(string md, HashSet<string> seen)
    {
        var sb = new StringBuilder();
        var fenced = false;
        foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }

            var key = line.Trim().ToLowerInvariant();
            if (!fenced && (SelfImport().IsMatch(line) || (key.Length >= MinLine && !seen.Add(key))))
            {
                continue;
            }

            if (line.Length == 0 && (sb.Length == 0 || sb.ToString().EndsWith("\n\n", StringComparison.Ordinal)))
            {
                continue;
            }

            sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*@\S*AGENTS\.md\s*$")] private static partial Regex SelfImport();
}
