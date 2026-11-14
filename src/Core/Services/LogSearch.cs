// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace AiStackprint.Services;

/// <summary>A match: 1-based line number in the file, the line text (trimmed for display), and the match position within the full line.</summary>
public record SearchHit(long Line, string Text, int Column, int Length);

/// <summary>Searches a whole log file line by line (not just the part on screen). Opens it shared so the app can keep writing.</summary>
public static class LogSearch
{
    public const int MaxHits = 1000;
    private const int Block = 1 << 16;   // characters read at a time

    /// <summary>Builds the matcher for the raw file (<see cref="LogQuery"/>); throws ArgumentException for an invalid regex.</summary>
    public static LogQuery Pattern(string query, bool matchCase, bool regex) =>
        LogQuery.Parse(query, matchCase, regex) ?? throw new ArgumentException("Nothing to search for.", nameof(query));

    /// <summary>
    /// Lines that match, with the first match in each line. Reads the file in blocks and tests each line in place:
    /// only a matching line becomes a string, so a large file costs a block of memory, not a copy of every line.
    /// </summary>
    public static List<SearchHit> File(string path, LogQuery pattern, int max = MaxHits, CancellationToken ct = default)
    {
        var hits = new List<SearchHit>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        var buf = new char[Block];
        int len = 0;
        long n = 0;
        bool Line(ReadOnlySpan<char> line)   // false when there are enough hits
        {
            if ((++n & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (line.EndsWith("\r"))
            {
                line = line[..^1];
            }

            if (!pattern.IsMatch(line))
            {
                return true;
            }

            var text = new string(line);
            var (at, length) = pattern.First(text) ?? (0, 0);
            hits.Add(new SearchHit(n, Snippet(text, at, length), at, length));
            return hits.Count < max;
        }

        while (true)
        {
            var read = r.Read(buf, len, buf.Length - len);
            if (read == 0)
            {
                if (len > 0)
                {
                    Line(buf.AsSpan(0, len));   // the last line has no line break
                }

                return hits;
            }

            len += read;
            var start = 0;
            for (int nl; (nl = buf.AsSpan(start, len - start).IndexOf('\n')) >= 0; start += nl + 1)
            {
                if (!Line(buf.AsSpan(start, nl)))
                {
                    return hits;
                }
            }

            if (start > 0)
            {
                buf.AsSpan(start, len - start).CopyTo(buf);   // keep the unfinished line
                len -= start;
            }
            else if (len == buf.Length)
            {
                Array.Resize(ref buf, buf.Length * 2);   // a line longer than the block
            }
        }
    }

    /// <summary>Up to 160 characters around the match.</summary>
    public static string Snippet(string line, int at, int len)
    {
        if (line.Length <= 160)
        {
            return line.Trim();
        }

        int start = Math.Max(0, at - 60), end = Math.Min(line.Length, Math.Max(at + len + 60, start + 160));
        return (start > 0 ? "…" : "") + line[start..end].Trim() + (end < line.Length ? "…" : "");
    }

    /// <summary>Lines [from, from+count) of a file (1-based), for showing the part of a large file around a match.</summary>
    public static (string Text, long First) Window(string path, long around, int before = 2000, int after = 2000)
    {
        long first = Math.Max(1, around - before), n = 0;
        var sb = new StringBuilder();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        while (r.ReadLine() is { } line)
        {
            n++;
            if (n < first)
            {
                continue;
            }

            if (n > around + after)
            {
                break;
            }

            sb.Append(line).Append('\n');
        }
        return (sb.ToString(), first);
    }

    /// <summary>Number of lines before byte <paramref name="offset"/> (to know which file line the tail view starts at).</summary>
    public static long LinesBefore(string path, long offset)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[64 * 1024]; long count = 0, left = offset;
        while (left > 0)
        {
            var n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0)
            {
                break;
            }

            for (var i = 0; i < n; i++)
            {
                if (buf[i] == (byte)'\n')
                {
                    count++;
                }
            }

            left -= n;
        }
        return count;
    }
}
