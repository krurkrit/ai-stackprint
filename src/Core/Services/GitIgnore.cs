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
    private sealed record Rule(Regex Rx, bool Negate, bool DirOnly);

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

    private static bool? Match(List<Rule> rules, string path, bool dir)
    {
        bool? res = null;
        foreach (var r in rules.Where(r => (dir || !r.DirOnly) && r.Rx.IsMatch(path)))
        {
            res = !r.Negate;
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
                res.Add(new(Glob.ToRegex(l, anchored: l.Contains('/')), negate, dirOnly));   // a / at the start or in the middle: from this folder only
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
