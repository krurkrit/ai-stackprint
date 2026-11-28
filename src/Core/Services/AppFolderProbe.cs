// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Something found in an app's config folder.</summary>
/// <param name="Kind">skills, commands, agents, instructions, settings, mcp, hooks or logs.</param>
/// <param name="Rel">Path relative to the config folder ("" = the folder itself), with forward slashes.</param>
/// <param name="Folder">A folder (true) or a file.</param>
public sealed record FoundPart(string Kind, string Rel, bool Folder);

/// <summary>One row of the Add app dialog: an item of one of the app's lists in apps.json.</summary>
/// <param name="List">skills, configs, commands, agents or logs: the apps.json list it goes in.</param>
/// <param name="Path">As written in apps.json (~ for the home folder; Project and Local paths relative to a project folder).</param>
/// <param name="Level">System, Local, Project or User.</param>
/// <param name="Kind">configs only: instructions, settings, mcp, ignore or hooks.</param>
public sealed record AppEntry(string List, string Path, string Level, string Kind = "");

/// <summary>
/// For the Add app dialog: looks inside a config folder for the files and folders apps usually keep there (skills/, commands/, agents/,
/// AGENTS.md, mcp.json, logs/ …), turns them into rows, and builds the app from the rows. Read-only; looks only at the folder's own entries
/// and, for skills, a few levels down.
/// </summary>
public static class AppFolderProbe
{
    /// <summary>The apps.json lists the dialog edits, in the order apps.json has them.</summary>
    public static readonly string[] Lists = ["skills", "configs", "commands", "agents", "logs"];

    /// <summary>Kinds of a config file (<see cref="ConfigFile.Kind"/>).</summary>
    public static readonly string[] ConfigKinds = ["instructions", "settings", "mcp", "ignore", "hooks"];

    private static readonly (string Kind, string[] Names)[] _folders =
    [
        ("skills", ["skills"]),
        ("commands", ["commands", "prompts", "workflows"]),
        ("agents", ["agents", "subagents"]),
        ("instructions", ["rules", "instructions"]),
        ("hooks", ["hooks"]),
        ("logs", ["logs", "log"])
    ];

    private static readonly (string Kind, string[] Names)[] _files =
    [
        ("instructions", ["AGENTS.md", "CLAUDE.md", "GEMINI.md", "RULES.md", "instructions.md"]),
        ("mcp", ["mcp.json", "mcp_config.json", "mcp-config.json", ".mcp.json", "mcp_settings.json"]),
        ("settings", ["settings.json", "config.json", "config.toml", "config.yaml", "config.yml", "settings.toml"]),
        ("hooks", ["hooks.json"])
    ];

    /// <summary>What <paramref name="folder"/> holds, in a fixed order (skills, commands, agents, instructions, settings, MCP, hooks, logs); empty when it does not exist.</summary>
    public static List<FoundPart> Look(string folder)
    {
        var found = new List<FoundPart>();
        var path = PathUtil.Expand(folder.Trim());
        if (path == "" || !Directory.Exists(path))
        {
            return found;
        }

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(path).Select(System.IO.Path.GetFileName).OfType<string>().ToList();
            foreach (var (kind, names) in _folders)
            {
                found.AddRange(names.Where(n => entries.Exists(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase)) && Directory.Exists(System.IO.Path.Combine(path, n)))
                    .Select(n => new FoundPart(kind, Actual(entries, n), true)));
            }

            foreach (var (kind, names) in _files)
            {
                found.AddRange(names.Where(n => File.Exists(System.IO.Path.Combine(path, n)) && entries.Exists(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase)))
                    .Select(n => new FoundPart(kind, Actual(entries, n), false)));
            }

            if (!found.Exists(f => f.Kind == "skills") && HasSkill(path))
            {
                found.Insert(0, new FoundPart("skills", "", true));   // no skills/ folder: SKILL.md files somewhere in it
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        string[] order = ["skills", "commands", "agents", "instructions", "settings", "mcp", "hooks", "logs"];
        return [.. found.OrderBy(f => Array.IndexOf(order, f.Kind))];
    }

    /// <summary>Rows for what was found in <paramref name="folder"/>, at User level, with the home folder written as ~.</summary>
    public static List<AppEntry> Entries(string folder, IEnumerable<FoundPart> found)
    {
        var root = Tilde(folder.Trim().TrimEnd('/', '\\'));
        return [.. found.Select(f =>
        {
            var path = f.Rel == "" ? root : root.Replace('\\', '/') + "/" + f.Rel;
            return f.Kind switch
            {
                "skills" or "commands" or "agents" or "logs" => new AppEntry(f.Kind, path, "User"),
                _ => new AppEntry("configs", path, "User", f.Kind)
            };
        })];
    }

    /// <summary>The custom app: <paramref name="folder"/> detects it (written with ~), and each row with a path goes in its list.</summary>
    public static AppDefinition Build(string name, string category, string folder, IEnumerable<AppEntry> entries)
    {
        var def = new AppDefinition
        {
            Id = AppCatalog.CustomPrefix + new string([.. name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-'),
            Name = name.Trim(),
            Category = category,
            Detect = { Tilde(folder.Trim().TrimEnd('/', '\\')) }
        };
        foreach (var e in entries.Where(e => e.Path.Trim() != ""))
        {
            var path = Tilde(e.Path.Trim());
            switch (e.List)
            {
                case "skills":
                    def.Skills.Add(new SkillSource
                    {
                        Path = path,
                        Level = e.Level
                    });
                    break;
                case "commands":
                    def.Commands.Add(new PartSource
                    {
                        Path = path,
                        Level = e.Level,
                        Pattern = "*.md;*.toml"
                    });
                    break;
                case "agents":
                    def.Agents.Add(new PartSource
                    {
                        Path = path,
                        Level = e.Level
                    });
                    break;
                case "logs":
                    def.Logs.Add(new LogSource
                    {
                        Path = path,
                        Level = e.Level,
                        Depth = 2,
                        Format = "text-log"
                    });
                    break;
                default:
                    def.Configs.Add(new ConfigFile
                    {
                        Path = path,
                        Level = e.Level,
                        Kind = e.Kind == "" ? "instructions" : e.Kind
                    });
                    break;
            }
        }

        return def;
    }

    /// <summary>The entry's name as it is on disk (case).</summary>
    private static string Actual(List<string> entries, string name) => entries.First(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The home folder written as ~, so apps.json reads the same on another machine.</summary>
    private static string Tilde(string path)
    {
        var home = PathUtil.Home.TrimEnd('/', '\\');
        return home.Length > 1 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase) && (path.Length == home.Length || path[home.Length] is '/' or '\\')
            ? "~" + path[home.Length..].Replace('\\', '/')
            : path;
    }

    private static bool HasSkill(string path)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            IgnoreInaccessible = true
        };
        return Directory.EnumerateFiles(path, "SKILL.md", options).Any();
    }
}
