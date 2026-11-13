// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Generic, read-only scanner. App-specific knowledge lives in AppDefinition data and IPluginReader classes, not here.</summary>
public static class Scanner
{
    static readonly HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", ".venv", "venv", "__pycache__" };

    /// <summary>How deep "nested" project roots and files are searched below the project folder.</summary>
    public const int NestedDepth = 3;

    /// <summary>Levels whose paths are relative to a project folder.</summary>
    public static bool InProject(string level) => level is "Project" or "Local";

    public static ScanResult Collect(IEnumerable<AppDefinition> defs, IEnumerable<string> projects)
    {
        var rows = new List<Item>();
        var apps = new List<AppInfo>();
        var projectList = projects.ToList();

        foreach (var d in defs)
        {
            var folder = d.Detect.Select(PathUtil.Expand).FirstOrDefault(Directory.Exists);
            apps.Add(new AppInfo(d.Id, d.Name, d.Category, d.Logo, folder != null, folder));
            if (folder != null)
            {
                rows.Add(new Item("App", d.Name, d.Notes ?? "Config folder: " + folder, d.Name, "User", folder) { App = d.Id });
            }

            // Bundled roots first, so a skill found again by a broader root keeps its bundled flag after de-duplication
            foreach (var r in d.Roots.Where(r => PathUtil.OsMatches(r.Os)).OrderByDescending(r => r.Bundled))
            {
                Tagged(rows, () => ReadRoot(PathUtil.Expand(r.Path), r, d.Name, rows), i => i with { App = d.Id, LevelLabel = r.Label, Bundled = r.Bundled });
            }

            if (d.PluginManifest != null)
            {
                PluginReaders.Find(d.PluginFormat)?.Read(PathUtil.Expand(d.PluginManifest), d, rows);
            }

            foreach (var c in d.Configs.Where(c => !InProject(c.Level) && PathUtil.OsMatches(c.Os)))
            {
                AddConfig(rows, d, c, PathUtil.Expand(c.Path), null);
            }

            foreach (var p in projectList)
            {
                foreach (var r in d.ProjectRoots.Where(r => PathUtil.OsMatches(r.Os)))
                {
                    foreach (var path in Candidates(p, r.Path, r.Nested))
                    {
                        Tagged(rows, () => ReadRoot(path, r, d.Name, rows), i => i with { App = d.Id, Project = p, LevelLabel = r.Label });
                    }
                }

                foreach (var c in d.Configs.Where(c => InProject(c.Level) && PathUtil.OsMatches(c.Os)))
                {
                    foreach (var path in Candidates(p, c.Path, c.Nested))
                    {
                        AddConfig(rows, d, c, path, p);
                    }
                }
            }
        }
        return new ScanResult(rows.DistinctBy(i => (i.Kind, i.Name, i.Source, i.Level, i.Path)).ToList(), apps);
    }

    /// <summary>project/rel, plus sub-folder/rel for every sub-folder up to <see cref="NestedDepth"/> when nested (monorepos).</summary>
    public static IEnumerable<string> Candidates(string project, string rel, bool nested)
    {
        rel = rel.Replace('/', Path.DirectorySeparatorChar);
        yield return Path.Combine(project, rel);
        if (!nested)
        {
            yield break;
        }

        foreach (var dir in SubFolders(project, NestedDepth))
        {
            var p = Path.Combine(dir, rel);
            if (File.Exists(p) || Directory.Exists(p))
            {
                yield return p;
            }
        }
    }

    /// <summary>Sub-folders of a project, skipping hidden and build/dependency folders.</summary>
    static IEnumerable<string> SubFolders(string root, int depth)
    {
        if (depth == 0)
        {
            yield break;
        }

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || _skip.Contains(name))
            {
                continue;
            }

            yield return d;
            foreach (var s in SubFolders(d, depth - 1))
            {
                yield return s;
            }
        }
    }

    /// <summary>Runs <paramref name="read"/> and rewrites the rows it appended.</summary>
    public static void Tagged(List<Item> rows, Action read, Func<Item, Item> tag)
    {
        int start = rows.Count;
        read();
        for (int i = start; i < rows.Count; i++)
        {
            rows[i] = tag(rows[i]);
        }
    }

    static void AddConfig(List<Item> rows, AppDefinition d, ConfigFile c, string path, string? project)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var kind = c.Kind switch { "settings" => "Settings", "mcp" => "MCP servers", _ => "Instructions" };
        rows.Add(new Item("Config", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), kind, d.Name, c.Level, path)
        { App = d.Id, Project = project, LevelLabel = c.Label });
    }

    public static void ReadRoot(string path, ScanRoot r, string source, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            if (r.Layout == "flat-md")
            {
                foreach (var f in Directory.EnumerateFiles(path, "*.md"))
                {
                    rows.Add(ReadSkill(f, source, r.Level));
                }
            }
            else
            {
                Walk(path, r.Depth, source, r.Level, rows);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static void Walk(string dir, int depth, string source, string level, List<Item> rows)
    {
        try
        {
            var f = Path.Combine(dir, "SKILL.md");
            if (File.Exists(f))
            {
                rows.Add(ReadSkill(f, source, level));
            }

            if (depth == 0)
            {
                return;
            }

            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                if (!_skip.Contains(Path.GetFileName(d)))
                {
                    Walk(d, depth - 1, source, level, rows);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads name, description and invocation flags from the SKILL.md front matter.</summary>
    static Item ReadSkill(string file, string source, string level)
    {
        string name = Path.GetFileName(file).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(file)!) : Path.GetFileNameWithoutExtension(file);
        string desc = "", block = "";
        string? author = null;
        bool modelOff = false, userOff = false;
        try
        {
            var lines = File.ReadLines(file).Take(40).ToList();
            if (lines.Count > 0 && lines[0].Trim() == "---")
            {
                for (int i = 1; i < lines.Count && lines[i].Trim() != "---"; i++)
                {
                    var l = lines[i];
                    if (l.Length > 0 && !char.IsWhiteSpace(l[0]) && l.Contains(':'))
                    {
                        block = l[..l.IndexOf(':')].Trim();
                    }

                    if (l.StartsWith("name:"))
                    {
                        var n = Clean(l[5..]); if (n != "")
                        {
                            name = n;
                        }
                    }
                    else if (l.StartsWith("author:") || (block == "metadata" && l.Length > 0 && char.IsWhiteSpace(l[0]) && l.TrimStart().StartsWith("author:")))
                    {
                        var a = Clean(l.TrimStart()[7..]); if (a != "")
                        {
                            author = a;
                        }
                    }
                    else if (l.StartsWith("description:"))
                    {
                        desc = Clean(l[12..]);
                        // YAML block scalar: the text is on the next line
                        if (desc is "" or ">" or "|" or ">-" or "|-")
                        {
                            desc = i + 1 < lines.Count ? lines[i + 1].Trim() : "";
                        }
                    }
                    else if (l.StartsWith("disable-model-invocation:"))
                    {
                        modelOff = Clean(l[25..]).Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (l.StartsWith("user-invocable:"))
                    {
                        userOff = Clean(l[15..]).Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // Agent: the agent may load it; User: you can invoke it (e.g. /name). disable-model-invocation wins when both flags are set.
        string[] invokers = modelOff ? ["User"] : userOff ? ["Agent"] : ["Agent", "User"];
        return new Item("Skill", name, desc, source, level, file) { Invokers = invokers, Author = author };
    }

    static string Clean(string s) => s.Trim().Trim('"', '\'');
}
