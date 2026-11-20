// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Findings to review in installed skills and plugins, from patterns that are often used to hide or run things: hidden Unicode, text that tells the agent
/// to hide things from the user, commands that download and run code, long encoded blobs, links to bare IP addresses and paste sites, reads of credential
/// files, and plugin hooks that run missing scripts. Offline and read-only: nothing is run or fetched. No finding does not mean safe; a finding is a reason to read the file.
/// </summary>
public static partial class SafetyScan
{
    private const long MaxFile = 512 * 1024, MaxTotal = 4 * 1024 * 1024;
    private const int MaxLine = 5000, MaxFilesListed = 3;

    private static readonly string[] _textEndings = [".md", ".txt", ".json", ".jsonc", ".yml", ".yaml", ".toml", ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".py", ".js", ".mjs", ".cjs", ".ts", ".cmd", ".bat", ".rb", ".pl"];
    private static readonly string[] _proseEndings = [".md", ".txt"];

    private sealed record Rule(string Id, Regex Rx, bool Prose, bool Warning, string Text, bool SkipLong = true);

    private static readonly Rule[] _rules =
    [
        new("unicode", HiddenUnicode(), false, true, "has hidden Unicode (zero-width or direction-control characters): they can hide text from a reader", false),
        new("override", Override(), true, true, "has text that tells the agent to ignore earlier instructions or to hide things from you"),
        new("download-run", DownloadRun(), false, true, "downloads and runs code in one step (curl or wget into a shell, iwr into iex, encoded commands)"),
        new("blob", Blob(), false, false, "has a long encoded block (200+ base64 characters)", false),
        new("link", RiskyLink(), false, false, "links to a bare IP address or a paste site"),
        new("credentials", Credentials(), false, false, "mentions credential files (.ssh keys, .aws/credentials, .netrc …)")
    ];

    /// <summary>
    /// Findings for the skills and plugins in <paramref name="items"/>, as issues of the Safety area; each folder is read once.
    /// <paramref name="progress"/> gets (done, total) as the items are checked.
    /// </summary>
    public static List<HealthIssue> Check(IEnumerable<Item> items, string home, Action<int, int>? progress)
    {
        var res = new List<HealthIssue>();
        var byRoot = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
        var targets = items.Where(i => i.Kind is "Skill" or "Plugin").Select(i => (Item: i, Target: Target(i))).Where(t => t.Target.Root != null).ToList();
        var done = 0;
        foreach (var (i, (root, single)) in targets)
        {
            var key = single ? root! : root + Path.DirectorySeparatorChar;
            if (!byRoot.TryGetValue(key, out var findings))
            {
                byRoot[key] = findings = Folder(root!, single, home);
            }

            res.AddRange(findings.Select(f => new HealthIssue("Safety", i.Name, i.App, f, i)));
            progress?.Invoke(++done, targets.Count);
        }

        return res;
    }

    public static List<HealthIssue> Check(IEnumerable<Item> items, string home) => Check(items, home, null);

    /// <summary>What to read for an item: a skill folder (its SKILL.md's folder), a single skill file, or an agent plugin's folder (those that add skills, commands, agents, hooks or servers; not editor extensions).</summary>
    internal static (string? Root, bool Single) Target(Item i)
    {
        if (i.Path == "")
        {
            return (null, false);
        }

        if (i.Kind == "Plugin")
        {
            return Directory.Exists(i.Path) && i.Provides.Count > 0 ? (i.Path, false) : (null, false);
        }

        if (!File.Exists(i.Path))
        {
            return (null, false);
        }

        return Path.GetFileName(i.Path).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) ? (Path.GetDirectoryName(i.Path), false) : (i.Path, true);
    }

    private static List<Finding> Folder(string root, bool single, string home)
    {
        var files = single ? [SkillDoc.Entry(root)] : SkillDoc.Entries(root);
        var dir = single ? Path.GetDirectoryName(root)! : root;
        var hits = new Dictionary<string, List<string>>();
        long total = 0;
        foreach (var (rel, size, ticks) in files.Where(f => IsText(f.Rel) && f.Size <= MaxFile && !f.Rel.Contains("node_modules", StringComparison.OrdinalIgnoreCase)))
        {
            total += size;
            if (total > MaxTotal)
            {
                break;
            }

            var full = Path.Combine(dir, rel);
            foreach (var id in Scan(new FileStamp(full, size, ticks)))
            {
                if (!hits.TryGetValue(id, out var l))
                {
                    hits[id] = l = [];
                }

                l.Add(rel.Replace('\\', '/'));
            }
        }

        var res = new List<Finding>();
        foreach (var r in _rules.Where(r => hits.ContainsKey(r.Id)))
        {
            var list = hits[r.Id];
            res.Add(new($"{Names(list)} {r.Text}.", r.Warning));
        }

        if (!single)
        {
            HookFindings(root, home, res);
        }

        return res;
    }

    /// <summary>The command downloads and runs code in one step (the "download-run" rule).</summary>
    internal static bool DownloadsAndRuns(string command) => DownloadRun().IsMatch(command);

    private static string Names(List<string> files) => string.Join(", ", files.Take(MaxFilesListed)) + (files.Count > MaxFilesListed ? $" and {files.Count - MaxFilesListed} more" : "");

    private static bool IsText(string rel) => _textEndings.Any(e => rel.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string[] Scan(FileStamp file)
    {
        try
        {
            return ScanCache.Rule(file, f => Match(f.Path));   // the ids of the rules that matched: read again only when the file changed
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string[] Match(string file)
    {
        var text = ContextBudget.ReadText(file);
        var prose = _proseEndings.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        var minified = text.Split('\n').Any(l => l.Length > MaxLine);
        return [.. _rules.Where(r => (!r.Prose || prose) && (!r.SkipLong || !minified) && Hits(r, text)).Select(r => r.Id)];
    }

    private static bool Hits(Rule r, string text) => r.Id switch
    {
        "unicode" => r.Rx.IsMatch(text.TrimStart((char)0xFEFF)),
        "blob" => r.Rx.Matches(text).Any(m => !text.AsSpan(0, m.Index).EndsWith("base64,")),   // an embedded image is not a finding
        _ => r.Rx.IsMatch(text)
    };

    /// <summary>A plugin's hooks/hooks.json that runs a script the plugin does not contain.</summary>
    private static void HookFindings(string root, string home, List<Finding> res)
    {
        var file = Path.Combine(root, "hooks", "hooks.json");
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(ContextBudget.ReadText(file), ConfigJson.Jsonc);
            var missing = new List<string>();
            Commands(doc.RootElement, c =>
            {
                var cmd = c.Replace("${CLAUDE_PLUGIN_ROOT}", root, StringComparison.Ordinal).Replace("$CLAUDE_PLUGIN_ROOT", root, StringComparison.Ordinal);
                if (HookScript.Missing(cmd, root, home) is { } m)
                {
                    missing.Add(ConfigJson.Mask(m));
                }
            });
            if (missing.Count > 0)
            {
                res.Add(new($"hooks/hooks.json runs {string.Join(", ", missing.Distinct().Take(MaxFilesListed))}, which does not exist in the plugin.", true));
            }
        }
        catch (JsonException) { }
    }

    private static void Commands(JsonElement e, Action<string> found)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals("command") && p.Value.ValueKind == JsonValueKind.String)
                    {
                        found(p.Value.GetString()!);
                    }
                    else
                    {
                        Commands(p.Value, found);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var v in e.EnumerateArray())
                {
                    Commands(v, found);
                }

                break;
            default:
                break;
        }
    }

    [GeneratedRegex("[\u200B-\u200F\u202A-\u202E\u2060-\u2064\u2066-\u2069\uFEFF]|\uDB40[\uDC00-\uDC7F]")] private static partial Regex HiddenUnicode();

    [GeneratedRegex(@"ignore\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier)\s+(instructions|prompts?|rules)|disregard\s+(all\s+|any\s+)?(previous|prior|earlier|above)\s+(instructions|prompts?)|do\s+not\s+(tell|inform|let)\s+the\s+user|don'?t\s+(tell|inform|let)\s+the\s+user|without\s+(informing|telling|notifying|alerting)\s+the\s+user|(hide|conceal)\s+(this|it)\s+from\s+the\s+user", RegexOptions.IgnoreCase)]
    private static partial Regex Override();

    [GeneratedRegex(@"\b(curl|wget)\b[^\n|]*\|\s*(sudo\s+)?(ba|z|da)?sh\b|\b(iwr|irm|Invoke-WebRequest|Invoke-RestMethod)\b[^\n|]*\|\s*(iex|Invoke-Expression)\b|\bDownloadString\s*\(|-EncodedCommand\b|\bbase64\s+(-d|--decode)\b[^\n|]*\|\s*(ba)?sh\b", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRun();

    [GeneratedRegex(@"[A-Za-z0-9+/]{200,}={0,2}")] private static partial Regex Blob();

    [GeneratedRegex(@"https?://(?!(127|10|0)\.)(?!192\.168\.)(\d{1,3}\.){3}\d{1,3}|pastebin\.com/raw|paste\.ee/r/|hastebin\.com/raw|transfer\.sh/|ix\.io/", RegexOptions.IgnoreCase)]
    private static partial Regex RiskyLink();

    [GeneratedRegex(@"~/\.ssh\b|\.aws/credentials|\bid_(rsa|ed25519)\b|\.netrc\b|\.git-credentials|/etc/shadow", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();
}
