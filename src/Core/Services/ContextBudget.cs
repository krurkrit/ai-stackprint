// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>When an app puts a file into the model's context.</summary>
public enum LoadWhen { Always, Imported, OnDemand, Conditional, AgentDecides, Manual, Ignored }

/// <param name="Via">The file that imports this one (@path).</param>
/// <param name="Kept">Bytes or characters the app keeps when it cuts the file (null = all of it; 0 = not loaded).</param>
/// <param name="Counted">false when the app's limit does not count this file (Codex: the global AGENTS.md).</param>
/// <param name="Patterns">For <see cref="LoadWhen.Conditional"/>: the file patterns it applies to, relative to the project.</param>
public record ContextFile(string Path, LoadWhen Load, long Bytes, int Chars, string? Via = null, string? Note = null, long? Kept = null, bool Counted = true,
    IReadOnlyList<string>? Patterns = null)
{
    public int Tokens => Chars / 4;   // rough: about four characters per token
}

/// <param name="Unit">"bytes" (Codex counts bytes) or "characters".</param>
/// <param name="Limit">The app's limit for always-loaded instructions in that unit, or a warning threshold; null when none is documented.</param>
public record ContextReport(string AppId, string Unit, long? Limit, string? LimitNote, List<ContextFile> Files, List<Finding> Findings)
{
    /// <summary>Size of what loads at the start of every session and counts toward the limit, in <see cref="Unit"/>.</summary>
    public long AlwaysTotal => Files.Where(f => f.Counted && f.Load is LoadWhen.Always or LoadWhen.Imported).Sum(Measure);
    public long Measure(ContextFile f) => f.Kept ?? (Unit == "bytes" ? f.Bytes : f.Chars);
}

/// <summary>An app's rule for what it loads in a folder. To support a new app: add a class and list it in <see cref="ContextBudget.Rules"/>.</summary>
public interface IContextRule
{
    string Id { get; }
    /// <param name="project">The project folder.</param>
    /// <param name="start">Where the session starts (the project folder or one inside it).</param>
    /// <param name="home">The user's home folder.</param>
    ContextReport Read(string project, string start, string home);
}

/// <summary>Helpers shared by the rules: file sizes, @imports, folder walks. Read-only.</summary>
public static partial class ContextBudget
{
    public static readonly IContextRule[] Rules =
    [
        new ClaudeContext(), new CodexContext(), new GeminiContext(), new CopilotContext(), new CursorContext(), new WindsurfContext(), new KiroContext(), new QwenContext(),
        new OpenCodeContext(),
    ];
    public static IContextRule? Find(string? id) => id == null ? null : Rules.FirstOrDefault(r => r.Id == id);

    private static readonly HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", "build", "target", "venv", ".venv", "__pycache__" };

    public static ContextFile Measure(string path, LoadWhen load, string? via = null, string? note = null)
    {
        var text = ReadText(path);
        return new ContextFile(path, load, Encoding.UTF8.GetByteCount(text), text.Length, via, note);
    }

    public static string ReadText(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Folders from <paramref name="top"/> down to <paramref name="start"/> (both included); just start when it is not inside top.</summary>
    public static List<string> Chain(string top, string start)
    {
        var list = new List<string>();
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        {
            list.Add(d.FullName);
            if (Same(d.FullName, top)) { list.Reverse(); return list; }
        }
        return [start];
    }

    /// <summary>Folders from the file system root down to <paramref name="start"/>.</summary>
    public static List<string> Ancestors(string start)
    {
        var list = new List<string>();
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        {
            list.Add(d.FullName);
        }

        list.Reverse();
        return list;
    }

    /// <summary>The nearest folder at or above <paramref name="start"/> that holds one of <paramref name="markers"/> (e.g. .git); null when none does.</summary>
    public static string? RootOf(string start, IEnumerable<string> markers)
    {
        var m = markers.ToList();
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        {
            if (m.Any(x => Directory.Exists(Path.Combine(d.FullName, x)) || File.Exists(Path.Combine(d.FullName, x))))
            {
                return d.FullName;
            }
        }

        return null;
    }

    /// <summary>Files named one of <paramref name="names"/> in sub-folders below <paramref name="start"/> (not start itself), skipping hidden and build folders.</summary>
    public static IEnumerable<string> Below(string start, IReadOnlyCollection<string> names, int depth = 3)
    {
        if (depth == 0)
        {
            yield break;
        }

        List<string> dirs;
        try { dirs = [.. Directory.EnumerateDirectories(start)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in dirs.Order(StringComparer.OrdinalIgnoreCase))
        {
            var n = Path.GetFileName(d);
            if (n.StartsWith('.') || _skip.Contains(n))
            {
                continue;
            }

            // The folder's file names are read once and compared with the wanted names (a stat call per name and folder took most of the time in a big project)
            HashSet<string> present;
            try { present = Directory.EnumerateFiles(d).Select(f => Path.GetFileName(f)).ToHashSet(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { present = []; }
            foreach (var name in names.Where(present.Contains))
            {
                yield return Path.Combine(d, name);
            }

            foreach (var f in Below(d, names, depth - 1))
            {
                yield return f;
            }
        }
    }

    /// <summary>Markdown files under a folder (any depth), sorted.</summary>
    public static List<string> FilesIn(string dir, string pattern) =>
        Directory.Exists(dir) ? [.. Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Order(StringComparer.OrdinalIgnoreCase)] : [];

    public static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool Inside(string path, string folder)
    {
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(f, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [GeneratedRegex(@"```.*?```|~~~.*?~~~", RegexOptions.Singleline)] private static partial Regex Fenced();
    [GeneratedRegex(@"`[^`\n]*`")] private static partial Regex CodeSpan();
    [GeneratedRegex(@"(?<![\w@`""'/])@((?:\\ |[^\s`""'<>(){}\[\]])+)")] private static partial Regex ImportToken();

    /// <summary>Markdown without fenced blocks and code spans: what is left is prose, links and imports.</summary>
    public static string Prose(string md) => CodeSpan().Replace(Fenced().Replace(md, ""), "");

    /// <summary>@path imports in Markdown: not inside code spans or fenced blocks, not quoted; "\ " is a space in the path.</summary>
    public static List<string> Imports(string md)
    {
        var plain = Prose(md);
        return [.. ImportToken().Matches(plain).Select(m => m.Groups[1].Value.TrimEnd('.', ',', ';', ':', '!', '?').Replace("\\ ", " "))
            .Where(p => p.Length > 0).Distinct()];
    }

    /// <summary>An import's target: ~ is the home folder; relative paths are relative to the importing file.</summary>
    public static string ResolveImport(string import, string fromFile, string home)
    {
        var p = import.StartsWith("~/") || import.StartsWith("~\\") ? Path.Combine(home, import[2..]) : import;
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Path.GetDirectoryName(fromFile) ?? "", p));
    }

    /// <summary>
    /// Adds the files <paramref name="file"/> imports (recursively, up to <paramref name="maxHops"/>) as <see cref="LoadWhen.Imported"/>,
    /// with findings for missing files, cycles, too deep chains and imports from outside <paramref name="project"/>.
    /// </summary>
    public static void AddImports(string file, string home, string? project, int maxHops, List<ContextFile> files, List<Finding> findings, HashSet<string> seen, int hop = 1)
    {
        foreach (var imp in Imports(ReadText(file)))
        {
            string target;
            try { target = ResolveImport(imp, file, home); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            var name = Path.GetFileName(file);
            if (!File.Exists(target)) { findings.Add(new($"{name} imports @{imp}, which does not exist.")); continue; }
            if (hop > maxHops) { findings.Add(new($"{name} imports @{imp} more than {maxHops} hops deep; it is not loaded.")); continue; }
            if (!seen.Add(Path.GetFullPath(target))) { findings.Add(new($"@{imp} is imported more than once (or in a loop); it loads once.", false)); continue; }
            var note = project != null && Inside(file, project) && !Inside(target, project) ? "Outside the project: asks before loading the first time" : null;
            files.Add(Measure(target, LoadWhen.Imported, file, note));
            AddImports(target, home, project, maxHops, files, findings, seen, hop + 1);
        }
    }
}
