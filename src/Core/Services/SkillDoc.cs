// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// A SKILL.md (or other Markdown file) split into front matter and body, for the detail panel and the skill checks.
/// Front matter keys are flattened: "metadata.author"; lists become "a, b". Read-only.
/// </summary>
public partial record SkillDoc(IReadOnlyDictionary<string, string> Front, string Body, int Lines, bool HasFront, bool FrontClosed)
{
    /// <summary>Largest file read for the panel; bigger files are shown as their start.</summary>
    internal const int MaxBytes = 512 * 1024;

    public string Get(string key) => Front.TryGetValue(key, out var v) ? v : "";

    public static SkillDoc Read(string path)
    {
        string text;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(fs))
        {
            var buf = new char[(int)Math.Min(MaxBytes, fs.Length)];   // the file's size, not the limit: a 1 MB buffer per skill meant a full collection every few skills
            var n = r.ReadBlock(buf, 0, buf.Length);
            text = new string(buf, 0, n);
        }
        return Parse(text);
    }

    public static SkillDoc Parse(string text)
    {
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var count = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return new(new Dictionary<string, string>(), text, count, false, false);
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
        {
            return new(new Dictionary<string, string>(), text, count, true, false);
        }

        return new(ParseFront(lines[1..end]), string.Join('\n', lines[(end + 1)..]).TrimStart('\n'), count, true, true);
    }

    /// <summary>Small YAML subset: key: value, block scalars (| and >), nested maps one level down, and "- item" lists.</summary>
    private static Dictionary<string, string> ParseFront(string[] lines)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null, parent = null;
        var block = false;
        foreach (var raw in lines)
        {
            if (raw.Trim() == "" || raw.TrimStart().StartsWith('#'))
            {
                if (block && key != null) { d[key] += "\n"; }
                continue;
            }
            var indented = char.IsWhiteSpace(raw[0]);
            var t = raw.Trim();
            if (indented && block && key != null)
            {
                d[key] = (d[key] + " " + t).Trim();
                continue;
            }
            if (t.StartsWith("- ") && key != null && (indented || parent == key))
            {
                d[key] = d[key] == "" ? Clean(t[2..]) : d[key] + ", " + Clean(t[2..]);
                continue;
            }
            var m = KeyLine().Match(t);
            if (!m.Success)
            {
                continue;
            }

            string k = m.Groups[1].Value, v = m.Groups[2].Value.Trim();
            if (!indented)
            {
                parent = null;
            }

            if (indented && parent != null)
            {
                k = parent + "." + k;
            }

            block = v is "|" or ">" or "|-" or ">-" or "|+" or ">+";
            if (!indented && v == "")
            {
                parent = k;
            }

            key = k;
            d[k] = block ? "" : Clean(v);
        }
        foreach (var k in d.Keys.ToList())
        {
            d[k] = d[k].Trim();
        }

        return d;
    }

    private static string Clean(string s) => s.Trim().Trim('"', '\'').Trim();

    [GeneratedRegex(@"^([A-Za-z0-9_.-]+)\s*:(.*)$")] private static partial Regex KeyLine();
    [GeneratedRegex(@"!?\[[^\]\n]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")] private static partial Regex LinkTarget();

    /// <summary>Targets of Markdown links and images, in order, without duplicates.</summary>
    public static List<string> Links(string md) =>
        [.. LinkTarget().Matches(md).Select(m => m.Groups[1].Value).Distinct()];

    /// <summary>True for links that leave the app (web, mail).</summary>
    public static bool IsExternal(string link) => Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "mailto";

    /// <summary>
    /// A relative link from <paramref name="fromFile"/>, as a full path, when it stays inside <paramref name="root"/> (the skill or plugin folder).
    /// Null for web links, anchors, and anything outside the folder.
    /// </summary>
    public static string? Resolve(string root, string fromFile, string link)
    {
        if (IsExternal(link) || link.StartsWith('#'))
        {
            return null;
        }

        var target = Uri.UnescapeDataString(link.Split('#')[0].Split('?')[0]);
        if (target == "")
        {
            return null;
        }

        string full;
        try { full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fromFile) ?? root, target.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(r, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
    }

    /// <summary>Files in a skill or plugin folder (relative path, size), hidden folders skipped, at most <paramref name="max"/>.</summary>
    public static List<(string Rel, long Size)> Files(string root, int max = 300) => [.. Entries(root, max).Select(f => (f.Rel, f.Size))];

    /// <summary>One file as <see cref="Entries"/> lists it: its name, size and last-write time.</summary>
    public static (string Rel, long Size, long Ticks) Entry(string file)
    {
        var f = new FileInfo(file);
        return (f.Name, f.Length, f.LastWriteTimeUtc.Ticks);
    }

    /// <summary>
    /// <see cref="Files"/> with each file's last-write time (UTC ticks), taken from the folder listing itself: a stamp for <see cref="ScanCache"/> without asking for each file again.
    /// </summary>
    public static List<(string Rel, long Size, long Ticks)> Entries(string root, int max = 300)
    {
        var res = new List<(string, long, long)>();
        if (!Directory.Exists(root))
        {
            return res;
        }

        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opt))
        {
            var rel = Path.GetRelativePath(root, f.FullName);
            if (rel.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.') && p != ".claude-plugin" && p != ".mcp.json"))
            {
                continue;
            }

            res.Add((rel, f.Length, f.LastWriteTimeUtc.Ticks));
            if (res.Count >= max)
            {
                break;
            }
        }
        return [.. res.OrderBy(f => f.Item1.Count(c => c == Path.DirectorySeparatorChar)).ThenBy(f => f.Item1, StringComparer.OrdinalIgnoreCase)];
    }
}
