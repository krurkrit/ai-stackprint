// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Finds an app's log files and reads their end. Read-only; opens files shared so apps can keep writing.</summary>
public static partial class LogReader
{
    public static readonly int MaxFiles = 300;

    /// <summary>Log files for an app, newest first, capped at <see cref="MaxFiles"/>.</summary>
    /// <param name="usage">Also the usage-only sources (the Usage card); the Logs page leaves them out.</param>
    public static List<LogFile> Find(AppDefinition d, IEnumerable<string> projects, bool usage)
    {
        var found = new List<LogFile>();
        foreach (var s in (usage ? d.Logs.Concat(d.Usage) : d.Logs).Where(s => PathUtil.OsMatches(s.Os)))
        {
            if (s.Level == "Project")
            {
                foreach (var p in projects)
                {
                    Collect(Path.Combine(p, s.Path.Replace('/', Path.DirectorySeparatorChar)), s, d.Id, p, found);
                }
            }
            else
            {
                Collect(PathUtil.Expand(s.Path), s, d.Id, null, found);
            }
        }
        return [.. found.DistinctBy(f => f.Path).OrderByDescending(f => f.Modified).Take(MaxFiles)];
    }

    public static List<LogFile> Find(AppDefinition d, IEnumerable<string> projects) => Find(d, projects, false);

    /// <summary>Folders to watch for an app's logs: (folder, file pattern, include sub-folders). A source that is one file watches its folder for that name.</summary>
    public static List<(string Dir, string Pattern, bool Recursive)> Roots(AppDefinition d, IEnumerable<string> projects)
    {
        var roots = new List<(string, string, bool)>();
        void Add(string path, LogSource s)
        {
            if (Directory.Exists(path))
            {
                roots.Add((path, s.Pattern, s.Depth > 0));
            }
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            {
                roots.Add((dir, Path.GetFileName(path), false));
            }
        }
        foreach (var s in d.Logs.Where(s => PathUtil.OsMatches(s.Os)))
        {
            if (s.Level == "Project")
            {
                foreach (var p in projects)
                {
                    Add(Path.Combine(p, s.Path.Replace('/', Path.DirectorySeparatorChar)), s);
                }
            }
            else
            {
                Add(PathUtil.Expand(s.Path), s);
            }
        }
        return [.. roots.Distinct()];
    }

    /// <summary>Current size and time of one listed file (same rules as <see cref="Find(AppDefinition, IEnumerable{string}, bool)"/>, so a SQLite -wal counts); null when it is gone.</summary>
    public static LogFile? Restat(LogFile f)
    {
        var found = new List<LogFile>();
        Collect(f.Path, new LogSource
        {
            Label = f.Label,
            Format = f.Format,
            Account = f.Account
        }, f.App, f.Project, found);
        return found.FirstOrDefault();
    }

    private static void Collect(string path, LogSource s, string app, string? project, List<LogFile> found)
    {
        try
        {
            if (File.Exists(path))
            {
                Add(new FileInfo(path));
                return;
            }
            if (!Directory.Exists(path))
            {
                return;
            }

            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = s.Depth > 0,
                MaxRecursionDepth = s.Depth,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles(s.Pattern, opt))
            {
                Add(f);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        void Add(FileInfo f)
        {
            var size = f.Length;
            var time = f.LastWriteTime;
            if (f.Extension == ".db" && new FileInfo(f.FullName + "-wal") is { Exists: true } wal)
            {
                size += wal.Length;
                if (wal.LastWriteTime > time)
                {
                    time = wal.LastWriteTime;
                }
            }   // SQLite writes go to the -wal file first
            found.Add(new LogFile(app, s.Label, f.FullName, size, time, project, s.Format, s.Account));
        }
    }

    /// <summary>Last <paramref name="maxBytes"/> of a file as text, starting at a whole line. Truncated = earlier content was skipped.</summary>
    public static (string Text, bool Truncated) Tail(string path, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, fs.Length - maxBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[fs.Length - start];
        int n = 0, r;
        while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0)
        {
            n += r;
        }

        var from = 0;
        if (start > 0)
        {
            var nl = Array.IndexOf(buf, (byte)'\n', 0, n);
            from = nl < 0 ? 0 : nl + 1;
        }   // drop the partial first line
        return (Encoding.UTF8.GetString(buf, from, n - from), start > 0);
    }

    public static (string Text, bool Truncated) Tail(string path) => Tail(path, 256 * 1024);

    /// <summary>Lines of a file, one at a time, from the last <paramref name="maxBytes"/> (starting at a whole line). Nothing is held in memory but the current line.</summary>
    public static IEnumerable<string> Lines(string path, long maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        if (fs.Length > maxBytes)
        {
            fs.Seek(fs.Length - maxBytes, SeekOrigin.Begin);
            r.DiscardBufferedData();
            r.ReadLine();
        }   // drop the partial first line
        while (r.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    /// <summary>
    /// UTF-8 lines of a file from the last <paramref name="maxBytes"/> (starting at a whole line), not decoded to text: JSON lines are parsed
    /// straight from these bytes, about twice as fast as through strings. Each line is valid only until the next one is read.
    /// </summary>
    public static IEnumerable<ReadOnlyMemory<byte>> Utf8Lines(string path, long maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var partial = fs.Length > maxBytes;   // the first line read is cut off: drop it
        if (partial)
        {
            fs.Seek(fs.Length - maxBytes, SeekOrigin.Begin);
        }

        var buf = new byte[1 << 15];
        var len = 0;
        var first = !partial;
        while (true)
        {
            if (len == buf.Length)
            {
                Array.Resize(ref buf, buf.Length * 2);   // a line longer than the buffer
            }

            var read = fs.Read(buf, len, buf.Length - len);
            var end = len + read;
            var start = 0;
            if (first && end >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
            {
                start = 3;   // byte order mark
            }

            first = false;
            if (read == 0)
            {
                if (end > start && !partial)
                {
                    yield return Line(buf, start, end);   // the last line has no line break
                }

                yield break;
            }

            int nl;
            while ((nl = Array.IndexOf(buf, (byte)'\n', start, end - start)) >= 0)
            {
                if (!partial)
                {
                    yield return Line(buf, start, nl);
                }

                partial = false;
                start = nl + 1;
            }

            len = end - start;
            Buffer.BlockCopy(buf, start, buf, 0, len);
        }

        static ReadOnlyMemory<byte> Line(byte[] buf, int start, int end) => buf.AsMemory(start, end > start && buf[end - 1] == '\r' ? end - start - 1 : end - start);
    }

    /// <summary>Text lines as UTF-8, for parsers fed from memory (tests, pasted text).</summary>
    public static IEnumerable<ReadOnlyMemory<byte>> Utf8(IEnumerable<string> lines) => lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));

    /// <summary>Lines containing <paramref name="filter"/> (case-insensitive); all lines when empty.</summary>
    public static string Filter(string text, string filter) =>
        filter.Length == 0 ? text : string.Join('\n', text.Split('\n').Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)));
}
