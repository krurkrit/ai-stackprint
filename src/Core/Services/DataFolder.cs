// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>
/// The app's own files, in <c>~/.ai-stackprint</c> on every OS: your settings and lists at the top, <c>cache/</c> (scan cache, last scan),
/// <c>logs/</c> (crash and perf logs) and <c>backups/</c> (<c>configs/</c> before a save, <c>skills/</c> removed skills).
/// <see cref="Prepare"/> moves the files of older versions (%APPDATA%/AiStackprint, or ~/.config on macOS and Linux; AiProfiler before that).
/// </summary>
public static class DataFolder
{
    internal const string Note = "MOVED.txt";

    public static string Root => Path.Combine(PathUtil.Home, ".ai-stackprint");
    public static string Cache => Path.Combine(Root, "cache");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Backups => Path.Combine(Root, "backups");
    public static string ConfigBackups => Path.Combine(Backups, "configs");
    public static string SkillBackups => Path.Combine(Backups, "skills");

    /// <summary>Set when some old files could not be moved this start (they stay where they were and are tried again next start).</summary>
    public static string? MoveProblem { get; private set; }

    /// <summary>Before anything reads or writes the folder: moves the files of older versions into it.</summary>
    public static void Prepare()
    {
        var appData = PathUtil.SystemFolder(Environment.SpecialFolder.ApplicationData);
        var r = Move([Path.Combine(appData, "AiStackprint"), Path.Combine(appData, "AiProfiler")], Root);
        MoveProblem = r.Failed > 0
            ? $"{r.Failed} file(s) could not be moved to {Root}; they stay in the old folder and are tried again next start."
            : null;
    }

    public sealed record MoveResult(int Moved, int Kept, int Failed);

    /// <summary>
    /// Moves every file of the old folders to its place under <paramref name="root"/>. A file that already exists there is never overwritten:
    /// the old one stays. A file that cannot be copied stays too. The old folder keeps <see cref="Note"/> saying where the files went.
    /// Copy, then delete (works across drives); safe to run again.
    /// </summary>
    public static MoveResult Move(IEnumerable<string> oldFolders, string root)
    {
        int moved = 0, kept = 0, failed = 0;
        foreach (var old in oldFolders)
        {
            if (!Directory.Exists(old))
            {
                continue;
            }

            string[] files;
            try { files = Directory.GetFiles(old, "*", SearchOption.AllDirectories); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
                continue;
            }

            var left = new List<string>();
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(old, file);
                if (rel == Note)
                {
                    continue;
                }

                var dest = Path.Combine(root, Place(rel));
                if (File.Exists(dest))
                {
                    kept++;
                    left.Add(rel);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest);
                    File.Delete(file);
                    moved++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    left.Add(rel);
                }
            }

            if (files.Length > 0 && files.Any(f => Path.GetRelativePath(old, f) != Note))
            {
                RemoveEmptyFolders(old);
                WriteNote(old, root, left);
            }
        }
        return new MoveResult(moved, kept, failed);
    }

    private static readonly char[] _separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Where a file of an older version goes, from its path in the old folder.</summary>
    internal static string Place(string rel)
    {
        var parts = rel.Split(_separators);
        var rest = string.Join(Path.DirectorySeparatorChar, parts[1..]);
        return parts[0] switch
        {
            "scan-cache.json" or "last-scan.json" when parts.Length == 1 => Path.Combine("cache", parts[0]),
            "crash.log" or "crash.seen" or "perf.log" or "perf-report.txt" when parts.Length == 1 => Path.Combine("logs", parts[0]),
            "backups" when parts.Length > 1 => Path.Combine("backups", "configs", rest),
            "removed" when parts.Length > 1 => Path.Combine("backups", "skills", rest),
            _ => rel
        };
    }

    private static void RemoveEmptyFolders(string folder)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // an empty folder left behind is harmless
    }

    private static void WriteNote(string old, string root, List<string> left)
    {
        var text = $"AI Stackprint now keeps its files in {root}.\n" + (left.Count == 0
            ? "Everything was moved; this folder can be deleted.\n"
            : "These files were not moved (a file with the same name is already there, or it could not be copied); compare and delete them yourself:\n"
                + string.Concat(left.Select(l => "  " + l + "\n")));
        try { File.WriteAllText(Path.Combine(old, Note), text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the move itself is done; the note is a courtesy
    }
}
