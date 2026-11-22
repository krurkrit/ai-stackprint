// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Reads a window: its lines per part, its steps (with the files' own line numbers), or its text for the raw view.</summary>
public static partial class LogWindow
{
    /// <summary>Bytes read before a session window and parsed with it, so a tool result whose call is just above the window still pairs with it; their steps are left out.</summary>
    internal const long Overlap = 256 * 1024;

    /// <summary>The lines of one part in the window: <see cref="FirstLine"/> is the file line of the first row; steps that start before <see cref="Keep"/> are only context.</summary>
    internal sealed record Segment(string Path, long FirstLine, long Keep, List<ReadOnlyMemory<byte>> Rows);

    internal static List<Segment> Segments(LogSpan s, long overlap)
    {
        var list = new List<Segment>();
        for (var i = 0; i < s.Parts.Count; i++)
        {
            long start = s.PartStart(i), end = start + s.Sizes[i];
            if (s.Sizes[i] == 0 || end <= s.From || start >= s.To)
            {
                continue;
            }

            var path = s.Parts[i];
            long from = Math.Max(s.From, start) - start, to = Math.Min(s.To, end) - start;
            var keep = from == 0 ? 1 : LineIndex.LinesBefore(path, from) + 1;
            var read = list.Count == 0 && overlap > 0 && from > 0 ? Back(path, Math.Max(0, from - overlap)) : from;
            var first = read == from ? keep : read == 0 ? 1 : LineIndex.LinesBefore(path, read) + 1;
            list.Add(new Segment(path, first, keep, ReadRows(path, read, to)));
        }

        return list;
    }

    /// <summary>The lines of bytes <paramref name="from"/> to <paramref name="to"/> of a file (both at line starts), without line breaks; a byte order mark at the start is skipped.</summary>
    internal static List<ReadOnlyMemory<byte>> ReadRows(string path, long from, long to)
    {
        using var fs = Open(path);
        to = Math.Min(to, fs.Length);
        var buf = new byte[Math.Max(0, to - from)];
        fs.Seek(from, SeekOrigin.Begin);
        var n = Fill(fs, buf, buf.Length);
        var start = from == 0 && n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF ? 3 : 0;
        var rows = new List<ReadOnlyMemory<byte>>();
        while (start < n)
        {
            var nl = Array.IndexOf(buf, (byte)'\n', start, n - start);
            var end = nl < 0 ? n : nl;
            rows.Add(buf.AsMemory(start, end > start && buf[end - 1] == '\r' ? end - start - 1 : end - start));
            start = end + 1;
        }

        return rows;
    }

    /// <summary>
    /// The steps of a window: a session parsed with a little of what comes before it (<see cref="Overlap"/>), or the lines of a text log, part by part.
    /// Lines are the files' own; a step from another part than <paramref name="f"/> has that file as its <see cref="LogEntry.Source"/>. Tokens count the steps in the window.
    /// A Claude Code session gets the steps of its sub-agents that ran in the window's time.
    /// </summary>
    public static Session Parse(LogFile f, LogSpan s)
    {
        var lines = LineLogs.Is(f.Format);
        var parser = lines ? null : LogParsers.Find(f.Format) ?? throw new InvalidOperationException("No parser for " + f.Format);
        var entries = new List<LogEntry>();
        var models = new HashSet<string>();
        long input = 0, output = 0, peak = 0, window = 0;
        foreach (var seg in Segments(s, lines ? 0 : Overlap))
        {
            var parsed = lines ? LineLogs.Parse(seg.Rows) : parser!.Parse(seg.Rows);
            var source = string.Equals(seg.Path, f.Path, StringComparison.OrdinalIgnoreCase) ? null : seg.Path;
            var dropped = false;
            var keeping = seg.FirstLine >= seg.Keep;
            foreach (var e in parsed.Entries)
            {
                var line = e.Line > 0 ? e.Line + seg.FirstLine - 1 : 0;
                if (line > 0)
                {
                    keeping = line >= seg.Keep;
                }

                if (!keeping)
                {
                    dropped = true;
                    continue;   // only context: it is before the window
                }

                entries.Add(e with { Line = line, Source = e.Source ?? source });
                if (dropped && e.Usage is { } u)
                {
                    input += u.Input;
                    output += u.Output;
                }
            }

            models.UnionWith(parsed.Models);
            if (!dropped)
            {
                input += parsed.InputTokens;
                output += parsed.OutputTokens;
            }

            peak = Math.Max(peak, parsed.PeakContext);
            window = Math.Max(window, parsed.ContextWindow);
        }

        var last = entries.LastOrDefault(e => e.Time != null)?.Time;
        var main = new Session(entries, models, input, output, entries.FirstOrDefault(e => e.Time != null)?.Time, last, peak, window);
        return f.Format == "claude-session" ? SessionAgents.Merge(main, f.Path, !s.AtStart, until: s.AtEnd ? null : last) : main;
    }

    /// <summary>The window as text (one file), and the file line it starts at.</summary>
    public static (string Text, long FirstLine) Text(LogSpan s)
    {
        var path = s.Parts[s.PartOf(s.From)];
        var start = s.PartStart(s.PartOf(s.From));
        long from = s.From - start, to = Math.Min(s.To - start, s.Sizes[s.PartOf(s.From)]);
        using var fs = Open(path);
        var buf = new byte[Math.Max(0, Math.Min(to, fs.Length) - from)];
        fs.Seek(from, SeekOrigin.Begin);
        var n = Fill(fs, buf, buf.Length);
        var skip = from == 0 && n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF ? 3 : 0;
        return (Encoding.UTF8.GetString(buf, skip, n - skip).Replace("\r\n", "\n"), from == 0 ? 1 : LineIndex.LinesBefore(path, from) + 1);
    }

    /// <summary>Where the window is in its log, for the viewer's header: "MB 3.8–8.8 of 10".</summary>
    public static string Where(LogSpan s) => $"{Mb(s.From)}–{Mb(s.To)} MB of {Mb(s.Total)} MB";

    private static string Mb(long b) => (b / 1048576.0).ToString(b < 10 << 20 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture);
}
