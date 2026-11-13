// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>A match: 1-based line number in the file, the line text (trimmed for display), and the match position within the full line.</summary>
public record SearchHit(long Line, string Text, int Column, int Length);

/// <summary>Searches a whole log file line by line (not just the part on screen). Opens it shared so the app can keep writing.</summary>
public static class LogSearch
{
    public const int MaxHits = 1000;

    /// <summary>Builds the matcher; throws ArgumentException for an invalid regex.</summary>
    public static Regex Pattern(string query, bool matchCase, bool regex) =>
        new(regex ? query : Regex.Escape(query), (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static List<SearchHit> File(string path, Regex pattern, int max = MaxHits, CancellationToken ct = default)
    {
        var hits = new List<SearchHit>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        long n = 0;
        while (r.ReadLine() is { } line)
        {
            n++;
            if ((n & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Match m;
            try { m = pattern.Match(line); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success)
            {
                continue;
            }

            hits.Add(new SearchHit(n, Snippet(line, m.Index, m.Length), m.Index, m.Length));
            if (hits.Count >= max)
            {
                break;
            }
        }
        return hits;
    }

    /// <summary>Up to 160 characters around the match.</summary>
    static string Snippet(string line, int at, int len)
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
            int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0)
            {
                break;
            }

            for (int i = 0; i < n; i++)
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
