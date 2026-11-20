// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="Apps">Ids of the apps it concerns; empty when it is about a file any app may read.</param>
/// <param name="File">The file it is about (full path), or null.</param>
public record ProjectIssue(IReadOnlyList<string> Apps, string? File, Finding Finding);

/// <summary>An MCP server or hook a project config file starts.</summary>
/// <param name="Kind">"MCP server", "LSP server" or "Hook".</param>
/// <param name="What">The command line (secret-looking parts hidden) or the server address.</param>
public record ProjectProgram(string Kind, string Name, string What, string File, string? Note);

/// <param name="Context">What each app loads in the project, as the context section shows it.</param>
public record ProjectCheck(List<(AppDefinition App, ContextReport Report)> Context, List<ProjectIssue> Issues, List<ProjectProgram> Programs, List<ProjectIgnore> Ignores);

/// <summary>
/// Checks of one project's AI files, beyond each app's context limits (<see cref="ContextBudget"/>): rules whose file patterns match nothing,
/// the same text loaded twice at launch, empty files, links to missing files, and in the project's config files (apps.json configs at
/// Project or Local level) secrets, MCP servers and hooks that run programs, and permission settings that skip prompts; personal files that
/// git does not ignore. Read-only.
/// </summary>
public static partial class ProjectChecks
{
    private const int MinLine = 12, MinShared = 8;

    public static ProjectCheck Run(string project, string start, IReadOnlyList<AppDefinition> defs, string home)
    {
        var context = defs.Select(d => (App: d, Rule: ContextBudget.Find(d.Context))).Where(a => a.Rule != null)
            .Select(a => (a.App, a.Rule!.Read(project, start, home))).ToList();
        var issues = new List<ProjectIssue>();
        // One walk of the project folder for every check that needs its files (a big project took 0.8 s to list, and it was listed twice)
        var listed = new Lazy<(List<string> Files, bool Complete)>(() =>
        {
            var l = ProjectFiles.List(project, out var c);
            return (l, c);
        });
        Patterns(project, context, listed, issues);
        Duplicates(context, issues);
        Empty(project, context, issues);
        Links(project, context, issues);
        Personal(project, context, defs, home, issues);
        Shared(project, context, defs, issues);
        if (ContextBudget.Same(start, project))
        {
            Nested(project, context, defs, home, issues);   // every other folder a session may start in
        }

        var ignores = new List<ProjectIgnore>();
        var (files, complete) = listed.Value;
        if (complete)
        {
            Ignores(project, defs, files, issues, ignores);
            Paths(project, context, files, issues);
        }
        var programs = new List<ProjectProgram>();
        Configs(project, defs, issues, programs);
        return new(context, issues, programs, ignores);
    }

    /// <summary>Health page: every project's checks and the apps' own context findings, as issues of the Projects area.</summary>
    public static List<HealthIssue> ForHealth(IReadOnlyList<AppDefinition> defs, IEnumerable<string> projects, string home)
    {
        var res = new List<HealthIssue>();
        foreach (var p in projects.Where(Directory.Exists))
        {
            var name = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar));
            var check = Run(p, p, defs, home);
            foreach (var (app, r) in check.Context)
            {
                res.AddRange(r.Findings.Select(f => new HealthIssue("Projects", name, app.Id, f) { Project = p }));
            }

            res.AddRange(check.Issues.Select(i => new HealthIssue("Projects", name, i.Apps.Count == 1 ? i.Apps[0] : "", i.Finding) { Project = p }));
        }

        return res;
    }

    /// <summary>A rule that loads only for matching files, whose patterns match no file in the project: it never loads.</summary>
    private static void Patterns(string project, List<(AppDefinition App, ContextReport Report)> context, Lazy<(List<string> Files, bool Complete)> listed, List<ProjectIssue> issues)
    {
        var rules = context.SelectMany(c => c.Report.Files.Where(f => f.Load == LoadWhen.Conditional && f.Patterns is { Count: > 0 }).Select(f => (c.App, File: f))).ToList();
        if (rules.Count == 0)
        {
            return;
        }

        var (files, complete) = listed.Value;
        if (!complete)
        {
            return;   // too many files to be sure
        }

        foreach (var g in rules.GroupBy(r => r.File.Path, StringComparer.OrdinalIgnoreCase))
        {
            var f = g.First().File;
            if (!Glob.AnyMatch(f.Patterns!, files))
            {
                issues.Add(new([.. g.Select(r => r.App.Id).Distinct()], f.Path,
                    new($"{Rel(project, f.Path)} never loads: {string.Join(", ", f.Patterns!)} matches no file in this project.")));
            }
        }
    }

    /// <summary>Two files an app loads at launch that hold the same text: it is in context twice.</summary>
    private static void Duplicates(List<(AppDefinition App, ContextReport Report)> context, List<ProjectIssue> issues)
    {
        var lines = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> Of(string path)
        {
            if (!lines.TryGetValue(path, out var set))
            {
                set = [.. ContextBudget.ReadText(path).Split('\n').Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length >= MinLine && l.Any(char.IsLetterOrDigit))];
                lines[path] = set;
            }

            return set;
        }

        var pairs = new Dictionary<(string A, string B), (int Shared, List<AppDefinition> Apps)>();
        foreach (var (app, r) in context)
        {
            var launch = r.Files.Where(f => f.Load is LoadWhen.Always or LoadWhen.Imported).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < launch.Count; i++)
            {
                for (var j = i + 1; j < launch.Count; j++)
                {
                    HashSet<string> a = Of(launch[i]), b = Of(launch[j]);
                    var shared = a.Count(b.Contains);
                    if (shared >= MinShared || (shared > 0 && shared * 2 >= Math.Min(a.Count, b.Count)))
                    {
                        var key = (launch[i], launch[j]);
                        if (!pairs.TryGetValue(key, out var v))
                        {
                            v = (shared, []);
                        }

                        v.Apps.Add(app);
                        pairs[key] = v;
                    }
                }
            }
        }

        foreach (var ((a, b), (shared, apps)) in pairs)
        {
            issues.Add(new([.. apps.Select(x => x.Id)], a, new($"{Path.GetFileName(a)} and {Path.GetFileName(b)} share {shared} lines; {string.Join(", ", apps.Select(x => x.Name))} load{(apps.Count == 1 ? "s" : "")} both " +
                "at launch, so that text is in context twice. Keep it in one file and import or link it from the other.", false)));
        }
    }

    private static void Empty(string project, List<(AppDefinition App, ContextReport Report)> context, List<ProjectIssue> issues)
    {
        foreach (var g in context.SelectMany(c => c.Report.Files.Where(f => ContextBudget.Inside(f.Path, project) && f.Chars < 200 && ContextBudget.ReadText(f.Path).Trim() == "")
            .Select(f => (c.App, f.Path))).GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new([.. g.Select(x => x.App.Id).Distinct()], g.Key, new($"{Rel(project, g.Key)} is empty.", false)));
        }
    }

    /// <summary>Markdown links from the project's instruction files to files that do not exist (code is skipped).</summary>
    private static void Links(string project, List<(AppDefinition App, ContextReport Report)> context, List<ProjectIssue> issues)
    {
        foreach (var path in context.SelectMany(c => c.Report.Files).Select(f => f.Path).Where(p => ContextBudget.Inside(p, project)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var missing = SkillDoc.Links(ContextBudget.Prose(ContextBudget.ReadText(path)))
                .Where(l => !SkillDoc.IsExternal(l) && !l.StartsWith('#') && !l.Contains("://") && !l.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                .Where(l => SkillDoc.Resolve(project, path, l) is { } t && !File.Exists(t) && !Directory.Exists(t)).ToList();
            if (missing.Count > 0)
            {
                issues.Add(new([], path, new($"{Rel(project, path)} links to {Plural(missing.Count, "file")} that {(missing.Count == 1 ? "does" : "do")} not exist: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? ", …" : "")}.")));
            }
        }
    }

    /// <summary>The path relative to the project, with /; paths outside it in full.</summary>
    public static string Rel(string project, string path) =>
        ContextBudget.Inside(path, project) ? Path.GetRelativePath(project, path).Replace('\\', '/') : path;

    private static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";
}
