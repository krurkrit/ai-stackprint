// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="Counts">What this project's scan found inside, by item kind (Skill, Command, Agent, Plugin) or config type (Settings, MCP servers, Instructions, Ignore).</param>
/// <param name="Capped">Files and Bytes stop at <see cref="AiFolders.MaxFiles"/> files.</param>
public record AiFolder(string Name, string Path, List<string> Apps, Dictionary<string, int> Counts, int Files, long Bytes, bool Capped);

/// <summary>The hidden folders AI apps keep in a project (.claude, .cursor, .github …), taken from the app definitions: which apps use each, what is inside, how big.</summary>
public static class AiFolders
{
    public const int MaxFiles = 2000;
    private static readonly HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git" };

    public static List<AiFolder> List(string project, IEnumerable<AppDefinition> defs, IEnumerable<Item> items)
    {
        var uses = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
        {
            var paths = d.ProjectRoots.Select(r => r.Path).Concat(d.Configs.Where(c => Scanner.InProject(c.Level)).Select(c => c.Path))
                .Concat(d.Parts.Where(p => Scanner.InProject(p.Level)).Select(p => p.Path)).Concat(d.Plugins.Where(p => Scanner.InProject(p.Level)).Select(p => p.Path));
            foreach (var p in paths)
            {
                var slash = p.IndexOf('/');
                if (slash > 1 && p[0] == '.')
                {
                    if (!uses.TryGetValue(p[..slash], out var set))
                    {
                        uses[p[..slash]] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    set.Add(d.Name);
                }
            }
        }

        var mine = items.Where(i => i.Project == project).ToList();
        var result = new List<AiFolder>();
        foreach (var (name, apps) in uses.OrderBy(u => u.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(project, name);
            if (!Directory.Exists(path))
            {
                continue;
            }

            var counts = new Dictionary<string, int>();
            foreach (var i in mine.Where(i => Inside(path, i.Path)))
            {
                var key = i.Kind == "Config" ? i.Description : i.Kind;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            var (files, bytes, capped) = Measure(path);
            result.Add(new AiFolder(name, path, [.. apps], counts, files, bytes, capped));
        }

        return result;
    }

    private static bool Inside(string folder, string file) => file.StartsWith(folder + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || file.Equals(folder, StringComparison.OrdinalIgnoreCase);

    private static (int Files, long Bytes, bool Capped) Measure(string path)
    {
        var files = 0;
        long bytes = 0;
        var pending = new Stack<string>([path]);
        try
        {
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var d in Directory.EnumerateDirectories(dir).Where(d => !_skip.Contains(System.IO.Path.GetFileName(d))))
                {
                    pending.Push(d);
                }

                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (++files > MaxFiles)
                    {
                        return (MaxFiles, bytes, true);
                    }

                    bytes += new FileInfo(f).Length;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return (files, bytes, false);
    }
}
