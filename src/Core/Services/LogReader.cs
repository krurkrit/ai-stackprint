using System.Text;
using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Finds an app's log files and reads their end. Read-only; opens files shared so apps can keep writing.</summary>
public static class LogReader
{
    public const int MaxFiles = 300;

    /// <summary>Log files for an app, newest first, capped at <see cref="MaxFiles"/>.</summary>
    /// <param name="usage">Also the usage-only sources (the Usage card); the Logs page leaves them out.</param>
    public static List<LogFile> Find(AppDefinition d, IEnumerable<string> projects, bool usage = false)
    {
        var found = new List<LogFile>();
        foreach (var s in (usage ? d.Logs.Concat(d.Usage) : d.Logs).Where(s => PathUtil.OsMatches(s.Os)))
        {
            if (s.Level == "Project")
                foreach (var p in projects) Collect(Path.Combine(p, s.Path.Replace('/', Path.DirectorySeparatorChar)), s, d.Id, p, found);
            else Collect(PathUtil.Expand(s.Path), s, d.Id, null, found);
        }
        return found.DistinctBy(f => f.Path).OrderByDescending(f => f.Modified).Take(MaxFiles).ToList();
    }

    /// <summary>Folders to watch for an app's logs: (folder, file pattern, include sub-folders). A source that is one file watches its folder for that name.</summary>
    public static List<(string Dir, string Pattern, bool Recursive)> Roots(AppDefinition d, IEnumerable<string> projects)
    {
        var roots = new List<(string, string, bool)>();
        void Add(string path, LogSource s)
        {
            if (Directory.Exists(path)) roots.Add((path, s.Pattern, s.Depth > 0));
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir)) roots.Add((dir, Path.GetFileName(path), false));
        }
        foreach (var s in d.Logs.Where(s => PathUtil.OsMatches(s.Os)))
        {
            if (s.Level == "Project") foreach (var p in projects) Add(Path.Combine(p, s.Path.Replace('/', Path.DirectorySeparatorChar)), s);
            else Add(PathUtil.Expand(s.Path), s);
        }
        return roots.Distinct().ToList();
    }

    /// <summary>Current size and time of one listed file (same rules as <see cref="Find"/>, so a SQLite -wal counts); null when it is gone.</summary>
    public static LogFile? Restat(LogFile f)
    {
        var found = new List<LogFile>();
        Collect(f.Path, new LogSource { Label = f.Label, Format = f.Format, Account = f.Account }, f.App, f.Project, found);
        return found.FirstOrDefault();
    }

    static void Collect(string path, LogSource s, string app, string? project, List<LogFile> found)
    {
        try
        {
            if (File.Exists(path)) { Add(new FileInfo(path)); return; }
            if (!Directory.Exists(path)) return;
            var opt = new EnumerationOptions { RecurseSubdirectories = s.Depth > 0, MaxRecursionDepth = s.Depth, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles(s.Pattern, opt)) Add(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        void Add(FileInfo f)
        {
            long size = f.Length; var time = f.LastWriteTime;
            if (f.Extension == ".db" && new FileInfo(f.FullName + "-wal") is { Exists: true } wal) { size += wal.Length; if (wal.LastWriteTime > time) time = wal.LastWriteTime; }   // SQLite writes go to the -wal file first
            found.Add(new LogFile(app, s.Label, f.FullName, size, time, project, s.Format, s.Account));
        }
    }

    /// <summary>Last <paramref name="maxBytes"/> of a file as text, starting at a whole line. Truncated = earlier content was skipped.</summary>
    public static (string Text, bool Truncated) Tail(string path, int maxBytes = 256 * 1024)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, fs.Length - maxBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[fs.Length - start];
        int n = 0, r;
        while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
        int from = 0;
        if (start > 0) { int nl = Array.IndexOf(buf, (byte)'\n', 0, n); from = nl < 0 ? 0 : nl + 1; }   // drop the partial first line
        return (Encoding.UTF8.GetString(buf, from, n - from), start > 0);
    }

    /// <summary>Lines of a file, one at a time, from the last <paramref name="maxBytes"/> (starting at a whole line). Nothing is held in memory but the current line.</summary>
    public static IEnumerable<string> Lines(string path, long maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        if (fs.Length > maxBytes) { fs.Seek(fs.Length - maxBytes, SeekOrigin.Begin); r.DiscardBufferedData(); r.ReadLine(); }   // drop the partial first line
        while (r.ReadLine() is { } line) yield return line;
    }

    /// <summary>Lines containing <paramref name="filter"/> (case-insensitive); all lines when empty.</summary>
    public static string Filter(string text, string filter) =>
        filter.Length == 0 ? text : string.Join('\n', text.Split('\n').Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)));
}
