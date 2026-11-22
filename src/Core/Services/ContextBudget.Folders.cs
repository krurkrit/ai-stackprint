// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Enumeration;

namespace AiStackprint.Services;

/// <summary>The folder walk of the rules: instruction files in the folders below a start folder.</summary>
public static partial class ContextBudget
{
    /// <summary>Files named one of <paramref name="names"/> in sub-folders below <paramref name="start"/> (not start itself), skipping hidden and build folders.</summary>
    /// <remarks>Each folder is listed once, for its sub-folders and the wanted names only: other file names are not made into strings (a project with many files was listed for every start folder and app).</remarks>
    public static IEnumerable<string> Below(string start, IReadOnlyCollection<string> names, int depth)
    {
        if (depth == 0)
        {
            return [];
        }

        var wanted = new HashSet<string>(names, OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        return Entries(start, wanted) is { } top ? Walk(top.Dirs, names, wanted, depth) : [];
    }

    public static IEnumerable<string> Below(string start, IReadOnlyCollection<string> names) => Below(start, names, 3);

    private static IEnumerable<string> Walk(List<string> dirs, IReadOnlyCollection<string> names, HashSet<string> wanted, int depth)
    {
        foreach (var d in dirs)
        {
            var e = Entries(d, wanted);
            foreach (var name in names.Where(n => e?.Files.Contains(n) == true))
            {
                yield return Path.Combine(d, name);
            }

            if (depth > 1 && e != null)
            {
                foreach (var f in Walk(e.Value.Dirs, names, wanted, depth - 1))
                {
                    yield return f;
                }
            }
        }
    }

    /// <summary>A folder's sub-folders (not hidden or build folders; in name order) and which of the wanted file names it holds; null when it cannot be read.</summary>
    private static (List<string> Dirs, HashSet<string> Files)? Entries(string folder, HashSet<string> wanted)
    {
        var look = wanted.GetAlternateLookup<ReadOnlySpan<char>>();
        var dirs = new List<string>();
        var files = new HashSet<string>(wanted.Comparer);
        try
        {
            var entries = new FileSystemEnumerable<string>(folder, (ref FileSystemEntry e) => e.IsDirectory ? e.ToFullPath() : look.TryGetValue(e.FileName, out var n) ? n : "",
                new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => e.IsDirectory ? e.FileName.Length > 0 && e.FileName[0] != '.' && !_skipLookup.Contains(e.FileName) : look.Contains(e.FileName)
            };
            foreach (var p in entries)
            {
                if (wanted.Contains(p))
                {
                    files.Add(p);
                }
                else
                {
                    dirs.Add(p);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        dirs.Sort(StringComparer.OrdinalIgnoreCase);
        return (dirs, files);
    }
}
