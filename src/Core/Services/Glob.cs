// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// File patterns as instruction files use them (Claude Code rules paths:, Copilot applyTo, Cursor and Windsurf globs, Kiro fileMatchPattern,
/// OpenCode instructions): ** spans folders, * and ? stay within a name, {a,b} and [abc]. Matched against paths relative to the project,
/// with /. A pattern without a / also matches a file name at any depth. Case is ignored, so a check never reports a rule that does match.
/// </summary>
public static class Glob
{
    /// <summary>A front-matter value as a list of patterns: split at commas outside braces; quotes and [ ] of a YAML list removed.</summary>
    public static List<string> Split(string value)
    {
        var v = value.Trim();
        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            v = v[1..^1];
        }

        var res = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var c in v)
        {
            if (c == ',' && depth == 0)
            {
                Add();
                continue;
            }
            depth += c == '{' ? 1 : c == '}' && depth > 0 ? -1 : 0;
            sb.Append(c);
        }

        Add();
        return res;

        void Add()
        {
            var p = sb.ToString().Trim().Trim('"', '\'').Trim();
            if (p != "")
            {
                res.Add(p);
            }

            sb.Clear();
        }
    }

    /// <param name="anchored">Match from the start only, even without a / (.gitignore "/name").</param>
    public static Regex ToRegex(string pattern, bool anchored)
    {
        var p = pattern.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        var sb = new StringBuilder("^");
        if (!anchored && !p.Contains('/'))
        {
            sb.Append("(?:.*/)?");   // "*.ts" matches at any depth
        }

        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '*' && i + 1 < p.Length && p[i + 1] == '*')
            {
                var slash = i + 2 < p.Length && p[i + 2] == '/';
                sb.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (c == '*') { sb.Append("[^/]*"); }
            else if (c == '?') { sb.Append("[^/]"); }
            else if (c == '{') { sb.Append("(?:"); }
            else if (c == '}') { sb.Append(')'); }
            else if (c == ',' && Open(p, i)) { sb.Append('|'); }
            else if (c == '[' && p.IndexOf(']', i + 1) is var end && end > i)
            {
                var set = p[(i + 1)..end];
                sb.Append('[').Append(set.StartsWith('!') ? "^" + Regex.Escape(set[1..]) : Regex.Escape(set)).Append(']');
                i = end;
            }
            else { sb.Append(Regex.Escape(c.ToString())); }
        }

        try
        {
            return new Regex(sb.Append("(?:/.*)?$").ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);   // a folder matches the files inside it
        }
        catch (ArgumentException)
        {
            return new Regex(".*");   // unbalanced braces or brackets: counted as matching, so no rule is reported by mistake
        }
    }

    public static Regex ToRegex(string pattern) => ToRegex(pattern, false);

    /// <summary>True when the comma at <paramref name="at"/> is inside braces.</summary>
    private static bool Open(string p, int at) => p[..at].Count(c => c == '{') > p[..at].Count(c => c == '}');

    /// <summary>True when any of <paramref name="patterns"/> matches one of <paramref name="files"/>.</summary>
    public static bool AnyMatch(IEnumerable<string> patterns, IReadOnlyList<string> files) =>
        patterns.Select(ToRegex).Any(r => files.Any(r.IsMatch));
}

/// <summary>The files of a project folder, for checks that need them. Read-only.</summary>
public static class ProjectFiles
{
    private static readonly HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "build", "target", "venv", ".venv", "__pycache__", ".next", ".nuxt", ".gradle", ".turbo",
    };

    private static readonly FileAttributes _skipped = PathUtil.Skipped;

    private static readonly TimeSpan _keep = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<(string, int), (DateTime At, Lazy<(List<string> Files, bool Complete)> Listing)> _recent = [];

    /// <summary>
    /// Paths relative to <paramref name="project"/>, with /; version control, dependency and build folders skipped. The checks of one scan ask for the same list
    /// several times (each rule set, in parallel): a list is walked once and kept for 30 seconds, or until <see cref="Forget"/> (a new scan).
    /// The list returned is shared: do not change it.
    /// </summary>
    /// <param name="complete">False when the project has more than <paramref name="max"/> files and the list stops there.</param>
    public static List<string> List(string project, out bool complete, int max)
    {
        Lazy<(List<string> Files, bool Complete)> listing;
        lock (_recent)
        {
            var key = (Path.GetFullPath(project), max);
            if (!_recent.TryGetValue(key, out var hit) || DateTime.UtcNow - hit.At > _keep)
            {
                hit = (DateTime.UtcNow, new(() =>
                {
                    var l = Walk(project, out var c, max);
                    return (l, c);
                }));
                _recent[key] = hit;
            }

            listing = hit.Listing;
        }

        (var files, complete) = listing.Value;
        return files;
    }

    public static List<string> List(string project, out bool complete) => List(project, out complete, 20_000);

    /// <summary>Drops the kept lists: the next ask walks the folder again.</summary>
    public static void Forget() { lock (_recent) { _recent.Clear(); } }

    /// <summary>
    /// One native pass over the folder tree (a folder per call and a relative-path call per file took 0.8 s for 20,000 files; this takes about 0.2 s).
    /// Hidden (Windows only) and system files and unreadable folders are skipped.
    /// </summary>
    private static List<string> Walk(string project, out bool complete, int max)
    {
        var res = new List<string>();
        var root = Path.TrimEndingDirectorySeparator(project).Length + 1;
        var files = new System.IO.Enumeration.FileSystemEnumerable<string>(project,
            (ref System.IO.Enumeration.FileSystemEntry e) => string.Concat(e.Directory.Length > root ? string.Concat(e.Directory[root..], "/") : "", e.FileName).Replace('\\', '/'),
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = _skipped
            })
        {
            ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => !e.IsDirectory,
            ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => !_skip.Contains(e.FileName.ToString())
        };
        complete = true;
        try
        {
            foreach (var f in files)
            {
                if (res.Count >= max)
                {
                    complete = false;
                    return res;
                }

                res.Add(f);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return res;
    }
}
