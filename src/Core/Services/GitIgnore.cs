// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// Whether git ignores a file, read from the ignore files themselves (no git process): the global excludes file (core.excludesFile in
/// ~/.gitconfig, else $XDG_CONFIG_HOME/git/ignore or ~/.config/git/ignore), .git/info/exclude, then .gitignore in the repository root and in
/// each folder down to the file. The last matching pattern wins, ! takes a file back, a trailing / matches folders only, and a file in an
/// ignored folder stays ignored. Files git already tracks are not looked up. Case is ignored, as git does on Windows and macOS.
/// </summary>
public sealed partial class GitIgnore
{
    private sealed record Rule(Regex Rx, bool Negate, bool DirOnly, string Text);

    private readonly string _repo;
    private readonly List<Rule> _base;   // global excludes, then info/exclude: relative to the repository root
    private readonly Dictionary<string, List<Rule>> _dirs = new(StringComparer.OrdinalIgnoreCase);   // folder ("" = root) → its .gitignore

    public GitIgnore(string repo, string home)
    {
        _repo = repo;
        _base = [.. Parse(ContextBudget.ReadText(GlobalFile(home))), .. Parse(ContextBudget.ReadText(Path.Combine(repo, ".git", "info", "exclude")))];
    }

    /// <summary>The repository folder at or above <paramref name="folder"/> (it has .git), or null.</summary>
    public static string? RepoOf(string folder) => ContextBudget.RootOf(folder, [".git"]);

    /// <param name="rel">Path relative to the repository folder, with /.</param>
    public bool Ignored(string rel)
    {
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= parts.Length; i++)
        {
            var dir = i < parts.Length;
            var ignored = Match(_base, string.Join('/', parts[..i]), dir);
            for (var d = 0; d < i; d++)   // the root's .gitignore first; one nearer the path wins
            {
                ignored = Match(Rules(string.Join('/', parts[..d])), string.Join('/', parts[d..i]), dir) ?? ignored;
            }

            if (ignored == true)
            {
                return true;   // a folder git ignores: nothing inside it can be taken back
            }
        }

        return false;
    }

    /// <summary>How many of <paramref name="files"/> (paths relative to the folder the ignore file sits in, with /) the patterns in <paramref name="text"/> hide, and the patterns that match none of them.</summary>
    /// <remarks>One pass: each folder is matched once (files share folders), and each file once; a pattern that matched anything is marked on the way.</remarks>
    public static IgnoreResult Apply(string text, IReadOnlyList<string> files)
    {
        var rules = Parse(text);
        var used = new bool[rules.Count];
        var folders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();   // folder → it or a folder above it is ignored
        var hidden = 0;
        foreach (var f in files)
        {
            var inIgnored = false;
            for (var at = f.IndexOf('/'); at >= 0; at = f.IndexOf('/', at + 1))
            {
                var folder = f.AsSpan(0, at);
                if (!folders.TryGetValue(folder, out var ignored))
                {
                    ignored = Match(rules, folder, true, used) == true || inIgnored;   // matched even below an ignored folder: its patterns are not dead
                    folders[folder] = ignored;
                }

                inIgnored = ignored;
            }

            if (Match(rules, f, false, used) == true || inIgnored)   // an ignored folder cannot be re-included
            {
                hidden++;
            }
        }

        var dead = rules.Where((r, i) => !r.Negate && !used[i]).Select(r => r.Text).Distinct().ToList();
        return new(hidden, dead);
    }

    private static bool? Match(List<Rule> rules, ReadOnlySpan<char> path, bool dir, bool[]? used = null)
    {
        bool? res = null;
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if ((dir || !r.DirOnly) && r.Rx.IsMatch(path))
            {
                res = !r.Negate;   // the last matching pattern wins
                used?[i] = true;
            }
        }

        return res;
    }

    private List<Rule> Rules(string folder)
    {
        if (!_dirs.TryGetValue(folder, out var rules))
        {
            rules = Parse(ContextBudget.ReadText(Path.Combine(_repo, folder.Replace('/', Path.DirectorySeparatorChar), ".gitignore")));
            _dirs[folder] = rules;
        }

        return rules;
    }

    private static List<Rule> Parse(string text)
    {
        var res = new List<Rule>();
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            l = l.EndsWith("\\ ", StringComparison.Ordinal) ? l : l.TrimEnd();
            if (l.Length == 0 || l[0] == '#')
            {
                continue;
            }

            var negate = l[0] == '!';
            l = negate ? l[1..] : l;
            l = l.StartsWith("\\#", StringComparison.Ordinal) || l.StartsWith("\\!", StringComparison.Ordinal) ? l[1..] : l;
            var dirOnly = l.EndsWith('/');
            l = l.TrimEnd('/');
            if (l.Length > 0)
            {
                res.Add(new(Glob.ToRegex(l, anchored: l.Contains('/')), negate, dirOnly, (negate ? "!" : "") + l + (dirOnly ? "/" : "")));   // a / at the start or in the middle: from this folder only
            }
        }

        return res;
    }

    private static string GlobalFile(string home)
    {
        if (ExcludesFile().Match(ContextBudget.ReadText(Path.Combine(home, ".gitconfig"))) is { Success: true } m)
        {
            var p = m.Groups[1].Value.Trim().Trim('"');
            return p.StartsWith('~') ? Path.Combine(home, p[1..].TrimStart('/', '\\')) : p;
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".config") : xdg, "git", "ignore");
    }

    [GeneratedRegex(@"(?im)^\s*excludesfile\s*=\s*(.+?)\s*$")] private static partial Regex ExcludesFile();
}

/// <param name="Hidden">Files the patterns hide.</param>
/// <param name="Dead">Patterns that match no file or folder.</param>
public record IgnoreResult(int Hidden, List<string> Dead);
