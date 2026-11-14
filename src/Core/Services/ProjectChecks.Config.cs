// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

/// <summary>
/// The project's config files (JSON): secrets, MCP servers, hooks and permission settings; values that look secret are never shown.
/// Personal files (CLAUDE.local.md, settings.local.json, …) that git does not ignore.
/// </summary>
public static partial class ProjectChecks
{
    private static void Configs(string project, IReadOnlyList<AppDefinition> defs, List<ProjectIssue> issues, List<ProjectProgram> programs)
    {
        var files = defs.SelectMany(d => d.Configs.Where(c => Scanner.InProject(c.Level) && c.Kind is "mcp" or "settings" && PathUtil.OsMatches(c.Os)).Select(c => (App: d, Config: c)))
            .GroupBy(x => x.Config.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        foreach (var g in files)
        {
            var path = Path.Combine(project, g.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !Path.GetExtension(path).StartsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(ContextBudget.ReadText(path), Jsonc); }
            catch (JsonException e) { issues.Add(new([.. g.Select(x => x.App.Id).Distinct()], path, new($"{g.Key} is not valid JSON (line {e.LineNumber + 1}): the app may ignore it."))); continue; }

            using (doc)
            {
                var apps = g.Select(x => x.App.Id).Distinct().ToList();
                var local = g.Any(x => x.Config.Level == "Local");
                Secrets(doc.RootElement, "", g.Key, path, apps, local, issues);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    programs.AddRange(McpServers.Parse(doc.RootElement, path).Select(s => new ProjectProgram("MCP server", s.Name, s.What, path, s.Note)));
                    Hooks(doc.RootElement, project, path, apps, programs, issues);
                    Permissions(doc.RootElement, g.Key, path, apps, issues);
                }
            }
        }

        foreach (var p in programs.Where(p => p.Note?.StartsWith(McpServers.NotPinned, StringComparison.Ordinal) == true))
        {
            issues.Add(new([], p.File, new($"MCP server \"{p.Name}\" in {Rel(project, p.File)} runs a package without a fixed version: each start can fetch a newer, unreviewed release.", false)));
        }
    }

    /// <summary>String values that look like keys or tokens, by their name (api_key, token, …) or their shape (sk-…, ghp_…).</summary>
    private static void Secrets(JsonElement e, string at, string rel, string path, List<string> apps, bool local, List<ProjectIssue> issues)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && LooksSecret(p.Name, p.Value.GetString()!))
                    {
                        issues.Add(new(apps, path, new($"{rel}: \"{at}{p.Name}\" holds what looks like a secret (value hidden). " + (local
                            ? "This personal file is usually not committed, but the key is stored in plain text."
                            : "Files in a project are often committed: read it from an environment variable instead."), !local)));
                    }
                    else
                    {
                        Secrets(p.Value, at + p.Name + ".", rel, path, apps, local, issues);
                    }
                }

                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var v in e.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.String && TokenShape().IsMatch(v.GetString()!))
                    {
                        issues.Add(new(apps, path, new($"{rel}: \"{at.TrimEnd('.')}[{i}]\" holds what looks like a secret (value hidden).", !local)));
                    }
                    else
                    {
                        Secrets(v, $"{at.TrimEnd('.')}[{i}].", rel, path, apps, local, issues);
                    }

                    i++;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Hooks: every "command" under a top-level "hooks" object, named by its event (and matcher).</summary>
    private static void Hooks(JsonElement root, string project, string path, List<string> apps, List<ProjectProgram> programs, List<ProjectIssue> issues)
    {
        if (!root.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var ev in hooks.EnumerateObject())
        {
            Walk(ev.Value, ev.Name);
        }

        void Walk(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray())
                {
                    Walk(x, name);
                }
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                var n = Str(e, "matcher") is { Length: > 0 } m ? $"{name} · {m}" : name;
                if (Str(e, "command") is { } cmd)
                {
                    programs.Add(new("Hook", n, Mask(cmd), path, Str(e, "type") is { } t && t != "command" ? t : null));
                    if (HookScript.Missing(cmd, project, PathUtil.Home) is { } missing)
                    {
                        issues.Add(new(apps, path, new($"Hook {n} in {Rel(project, path)} runs {Mask(missing)}, which does not exist.")));
                    }
                }

                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object))
                {
                    Walk(p.Value, n);
                }
            }
        }
    }

    /// <summary>Claude Code settings that skip permission prompts.</summary>
    private static void Permissions(JsonElement root, string rel, string path, List<string> apps, List<ProjectIssue> issues)
    {
        if (!root.TryGetProperty("permissions", out var perm) || perm.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (Str(perm, "defaultMode") == "bypassPermissions")
        {
            issues.Add(new(apps, path, new($"{rel} sets defaultMode to bypassPermissions: every action runs without asking.")));
        }

        foreach (var rule in Strings(perm, "allow").Where(r => r.Replace(" ", "", StringComparison.Ordinal) is "Bash" or "Bash(*)" or "Bash(*:*)" or "Bash(:*)"))
        {
            issues.Add(new(apps, path, new($"{rel} allows \"{rule}\": every shell command runs without asking.")));
        }
    }

    /// <summary>
    /// Personal files the apps read in the project (Local config files, and *.local.* instruction files such as CLAUDE.local.md,
    /// AGENTS.local.md, QWEN.local.md) that git does not ignore, so they would be committed with the project.
    /// </summary>
    private static void Personal(string project, List<(AppDefinition App, ContextReport Report)> context, IReadOnlyList<AppDefinition> defs, string home, List<ProjectIssue> issues)
    {
        if (GitIgnore.RepoOf(project) is not { } repo)
        {
            return;
        }

        var files = context.SelectMany(c => c.Report.Files.Where(f => Path.GetFileName(f.Path).Contains(".local.", StringComparison.OrdinalIgnoreCase)).Select(f => (c.App.Id, f.Path)))
            .Concat(defs.SelectMany(d => d.Configs.Where(c => c.Level == "Local" && PathUtil.OsMatches(c.Os))
                .Select(c => (d.Id, Path: Path.Combine(project, c.Path.Replace('/', Path.DirectorySeparatorChar))))))
            .Where(x => File.Exists(x.Path) && ContextBudget.Inside(x.Path, repo))
            .GroupBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var git = new GitIgnore(repo, home);
        foreach (var g in files.Where(g => !git.Ignored(Path.GetRelativePath(repo, g.Key).Replace('\\', '/'))))
        {
            issues.Add(new([.. g.Select(x => x.Id).Distinct()], g.Key,
                new($"{Rel(project, g.Key)} is a personal file, but git does not ignore it: add it to .gitignore so it is not committed with the project.")));
        }
    }
}
