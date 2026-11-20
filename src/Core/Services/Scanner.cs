// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Generic, read-only scanner. App-specific knowledge lives in AppDefinition data and IPluginReader classes, not here.</summary>
public static partial class Scanner
{
    private static readonly HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", ".venv", "venv", "__pycache__" };

    /// <summary>How deep "nested" project roots and files are searched below the project folder.</summary>
    internal const int NestedDepth = 3;

    /// <summary>Levels whose paths are relative to a project folder.</summary>
    public static bool InProject(string level) => level is "Project" or "Local";

    public static ScanResult Collect(IEnumerable<AppDefinition> defs, IEnumerable<string> projects)
    {
        var projectList = projects.ToList();
        // One app at a time is independent of the others (it only reads files): the apps are scanned side by side, and put back in their order so the result does not depend on timing
        var scanned = defs.AsParallel().AsOrdered().Select(d => CollectApp(d, projectList)).ToList();
        return new ScanResult([.. scanned.SelectMany(s => s.Rows).DistinctBy(i => (i.Kind, i.Name, i.Source, i.Level, i.Path))], [.. scanned.Select(s => s.App)]);
    }

    private static (AppInfo App, List<Item> Rows) CollectApp(AppDefinition d, List<string> projectList)
    {
        var rows = new List<Item>();
        var folder = d.Detect.Select(PathUtil.Expand).FirstOrDefault(Directory.Exists);
        var app = new AppInfo(d.Id, d.Name, d.Category, d.Logo, folder != null, folder);
        if (folder != null)
        {
            rows.Add(new Item("App", d.Name, d.Notes ?? "Config folder: " + folder, d.Name, "User", folder) { App = d.Id });
        }

        // Bundled roots first, so a skill found again by a broader root keeps its bundled flag after de-duplication
        foreach (var r in d.Roots.Where(r => PathUtil.OsMatches(r.Os)).OrderByDescending(r => r.Bundled))
        {
            Tagged(rows, () => ReadRoot(PathUtil.Expand(r.Path), r, d.Name, rows), i => i with
            {
                App = d.Id,
                LevelLabel = r.Label,
                Bundled = r.Bundled
            });
        }

        if (d.PluginManifest != null)
        {
            PluginReaders.Find(d.PluginFormat)?.Read(PathUtil.Expand(d.PluginManifest), new PluginContext(d, "User", null, null), rows);
        }

        foreach (var src in d.Plugins.Where(s => !InProject(s.Level) && PathUtil.OsMatches(s.Os)))
        {
            PluginReaders.Find(src.Format)?.Read(PathUtil.Expand(src.Path), new PluginContext(d, src.Level, null, src), rows);
        }

        foreach (var c in d.Configs.Where(c => !InProject(c.Level) && PathUtil.OsMatches(c.Os)))
        {
            AddConfig(rows, d, c, PathUtil.Expand(c.Path), null);
        }

        foreach (var s in d.Parts.Where(s => !InProject(s.Level) && PathUtil.OsMatches(s.Os)))
        {
            Tagged(rows, () => ReadParts(PathUtil.Expand(s.Path), s, d.Name, rows), i => i with { App = d.Id });
        }

        foreach (var p in projectList)
        {
            foreach (var r in d.ProjectRoots.Where(r => PathUtil.OsMatches(r.Os)))
            {
                foreach (var path in Candidates(p, r.Path, r.Nested))
                {
                    Tagged(rows, () => ReadRoot(path, r, d.Name, rows), i => i with
                    {
                        App = d.Id,
                        Project = p,
                        LevelLabel = r.Label
                    });
                }
            }

            foreach (var src in d.Plugins.Where(s => InProject(s.Level) && PathUtil.OsMatches(s.Os)))
            {
                PluginReaders.Find(src.Format)?.Read(Path.Combine(p, src.Path.Replace('/', Path.DirectorySeparatorChar)), new PluginContext(d, src.Level, p, src), rows);
            }

            foreach (var c in d.Configs.Where(c => InProject(c.Level) && PathUtil.OsMatches(c.Os)))
            {
                foreach (var path in Candidates(p, c.Path, c.Nested))
                {
                    AddConfig(rows, d, c, path, p);
                }
            }

            foreach (var s in d.Parts.Where(s => InProject(s.Level) && PathUtil.OsMatches(s.Os)))
            {
                Tagged(rows, () => ReadParts(Path.Combine(p, s.Path.Replace('/', Path.DirectorySeparatorChar)), s, d.Name, rows), i => i with
                {
                    App = d.Id,
                    Project = p
                });
            }
        }

        return (app, rows);
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
    private static IEnumerable<string> SubFolders(string root, int depth)
    {
        if (depth == 0)
        {
            yield break;
        }

        IEnumerable<string> dirs;
        try { dirs = [.. Directory.EnumerateDirectories(root)]; }
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
        var start = rows.Count;
        read();
        for (var i = start; i < rows.Count; i++)
        {
            rows[i] = tag(rows[i]);
        }
    }

    private static void AddConfig(List<Item> rows, AppDefinition d, ConfigFile c, string path, string? project)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var kind = c.Kind switch { "settings" => "Settings", "mcp" => "MCP servers", "ignore" => "Ignore", "hooks" => "Hooks", _ => "Instructions" };
        rows.Add(new Item("Config", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), kind, d.Name, c.Level, path)
        {
            App = d.Id,
            Project = project,
            LevelLabel = c.Label,
            AppWritten = c.AppWritten
        });
    }
}
