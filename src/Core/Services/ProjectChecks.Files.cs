// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="Hidden">Files in the project that the file's patterns hide from the app.</param>
public record ProjectIgnore(string File, IReadOnlyList<string> Apps, int Hidden, int Total);

public static partial class ProjectChecks
{
    private const int MaxPathsPerFile = 5;
    private static readonly string[] _pathExtensions = [".md", ".json", ".jsonc", ".yml", ".yaml", ".toml", ".ts", ".tsx", ".js", ".mjs", ".cs", ".csproj", ".sln", ".py", ".sh", ".ps1", ".txt", ".xml", ".html", ".css"];
    private static readonly HashSet<string> _generated = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "dist", "build", "out", "target", "node_modules", "venv", ".venv", ".git", ".next" };
    private static readonly string[] _placeholders = ["path/to", "your-", "my-", "example", "xxx", "foo", "<", ">", "..."];

    /// <summary>Ignore files of the apps (.cursorignore, .geminiignore …): how many files each hides, and patterns that match nothing.</summary>
    private static void Ignores(string project, IReadOnlyList<AppDefinition> defs, List<string> files, List<ProjectIssue> issues, List<ProjectIgnore> ignores)
    {
        var found = defs.SelectMany(d => d.Configs.Where(c => c.Kind == "ignore" && Scanner.InProject(c.Level) && PathUtil.OsMatches(c.Os)).Select(c => (App: d.Id, c.Path)))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var g in found)
        {
            var path = Path.Combine(project, g.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var apps = g.Select(x => x.App).Distinct().ToList();
            var r = GitIgnore.Apply(ContextBudget.ReadText(path), files);
            ignores.Add(new(path, apps, r.Hidden, files.Count));
            if (r.Dead.Count > 0)
            {
                issues.Add(new(apps, path, new($"{g.Key} has patterns that match no file or folder in this project: {string.Join(", ", r.Dead.Take(5))}{(r.Dead.Count > 5 ? $" and {r.Dead.Count - 5} more" : "")}.", false)));
            }
        }
    }

    /// <summary>Info: paths written as code in the instruction files that exist neither from the project folder nor from the file's folder.</summary>
    private static void Paths(string project, List<(AppDefinition App, ContextReport Report)> context, List<string> files, List<ProjectIssue> issues)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var docs = context.SelectMany(c => c.Report.Files.Where(f => f.Load != LoadWhen.Ignored && ContextBudget.Inside(f.Path, project) && f.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Select(f => (c.App.Id, f.Path)))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var g in docs)
        {
            var folder = Path.GetDirectoryName(g.Key) ?? project;
            var stale = Spans(ContextBudget.ReadText(g.Key)).Where(s => LooksLikePath(s) && !Exists(s, project, folder, files, set)).Distinct().ToList();
            if (stale.Count > 0)
            {
                issues.Add(new([.. g.Select(x => x.Id).Distinct()], g.Key,
                    new($"{Rel(project, g.Key)} mentions paths that do not exist: {string.Join(", ", stale.Take(MaxPathsPerFile))}{(stale.Count > MaxPathsPerFile ? $" and {stale.Count - MaxPathsPerFile} more" : "")}.", false)));
            }
        }
    }

    private static IEnumerable<string> Spans(string md) => CodeSpanText().Matches(FencedBlock().Replace(md, "")).Select(m => m.Groups[1].Value.Trim());

    private static bool LooksLikePath(string s)
    {
        if (s.Length < 3 || s.Length > 120 || s.Any(c => char.IsWhiteSpace(c) || c is '*' or '?' or '[' or ']' or '{' or '}' or '$' or '~' or '|' or '=' or '@' or '(' or ')' or ','))
        {
            return false;
        }

        var local = s[0] is '/' or '-' or '\\' || s.StartsWith("../", StringComparison.Ordinal);
        if (local || s.Contains("://", StringComparison.Ordinal) || s.Contains('\\') || (s.Contains(':') && !LineSuffix().IsMatch(s)))
        {
            return false;
        }

        if (_placeholders.Any(p => s.Contains(p, StringComparison.OrdinalIgnoreCase)) || _generated.Contains(s.Split('/')[0]))
        {
            return false;
        }

        if (s.Contains('/'))
        {
            return true;
        }

        // A bare file name: a known lowercase ending after a stem, and no Class.Member look (".Topic")
        return !s.StartsWith('.') && !UpperAfterDot().IsMatch(s) && _pathExtensions.Any(e => Clean(s).EndsWith(e, StringComparison.Ordinal));
    }

    /// <summary>Without a #anchor or :line suffix.</summary>
    private static string Clean(string s) => (LineSuffix().Replace(s.Split('#')[0], "") is var t && t.StartsWith("./", StringComparison.Ordinal) ? t[2..] : LineSuffix().Replace(s.Split('#')[0], "")).TrimEnd('/');

    private static bool Exists(string span, string project, string folder, List<string> files, HashSet<string> set)
    {
        var rel = Clean(span);
        if (rel.Length == 0 || File.Exists(Path.Combine(project, rel)) || Directory.Exists(Path.Combine(project, rel)) || File.Exists(Path.Combine(folder, rel)) || Directory.Exists(Path.Combine(folder, rel)))
        {
            return true;
        }

        return set.Contains(rel) || files.Any(f => f.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase) || f.EndsWith("/" + rel, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"`([^`\n]+)`")] private static partial Regex CodeSpanText();
    [GeneratedRegex(@"```.*?```|~~~.*?~~~", RegexOptions.Singleline)] private static partial Regex FencedBlock();
    [GeneratedRegex(@"\.[A-Z]")] private static partial Regex UpperAfterDot();
    [GeneratedRegex(@":\d+(-\d+)?$")] private static partial Regex LineSuffix();
}
