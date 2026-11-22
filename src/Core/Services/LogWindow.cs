// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;

namespace AiStackprint.Services;

/// <summary>
/// The part of a log that is shown: bytes <see cref="From"/> to <see cref="To"/> of its <see cref="Parts"/> read one after the other (oldest first; one file,
/// or the numbered parts of a text log, <see cref="LogParts"/>). Both ends are at the start of a line, and the start of each part counts as one.
/// </summary>
public sealed record LogSpan(IReadOnlyList<string> Parts, IReadOnlyList<long> Sizes, long From, long To)
{
    public long Total => Sizes.Sum();
    public bool AtStart => From <= 0;
    public bool AtEnd => To >= Total;

    /// <summary>Some of the log is not in the span.</summary>
    public bool Cut => !AtStart || !AtEnd;

    /// <summary>Byte where part <paramref name="i"/> starts.</summary>
    public long PartStart(int i) => Sizes.Take(i).Sum();

    /// <summary>The part a byte is in (the last part for the end).</summary>
    public int PartOf(long at)
    {
        long start = 0;
        for (var i = 0; i < Sizes.Count; i++)
        {
            if (at < start + Sizes[i])
            {
                return i;
            }

            start += Sizes[i];
        }

        return Sizes.Count - 1;
    }
}

/// <summary>
/// A moving window over a long log: it opens on the newest <see cref="Bytes"/>, and moves by a quarter of that (<see cref="Step"/>) toward the start or the end
/// as the view nears its top or bottom, so only one window's lines are read and parsed at a time. Read-only; files are opened shared.
/// </summary>
public static partial class LogWindow
{
    /// <summary>Bytes of a log a window holds for steps (Conversation, Compact, Lines).</summary>
    public const long Bytes = 5L << 20;

    /// <summary>How far a window moves: a quarter of its size, the part kept above (or below) the view when it moves.</summary>
    public static long Step(long bytes) => bytes / 4;

    private const int Block = 64 * 1024;

    /// <summary>The format is read line by line (a text log, JSON lines), so any window of it parses on its own; other formats are read whole or cut to their end.</summary>
    public static bool Fits(string? format) => LineLogs.Is(format) || LogParsers.Find(format ?? "")?.Feed() != null;

    /// <summary>The newest <paramref name="bytes"/> of the parts.</summary>
    public static LogSpan End(IReadOnlyList<string> parts, long bytes)
    {
        var sizes = Sizes(parts);
        var total = sizes.Sum();
        var s = new LogSpan(parts, sizes, 0, total);
        return s with { From = StartAtOrAfter(s, Math.Max(0, total - bytes)) };
    }

    /// <summary>The oldest <paramref name="bytes"/> of the parts.</summary>
    public static LogSpan Start(IReadOnlyList<string> parts, long bytes)
    {
        var s = new LogSpan(parts, Sizes(parts), 0, 0);
        return s with { To = EndAfter(s, 0, bytes) };
    }

    /// <summary>A window with line <paramref name="line"/> of part <paramref name="part"/> near its middle.</summary>
    public static LogSpan Around(IReadOnlyList<string> parts, int part, long line, long bytes)
    {
        var s = new LogSpan(parts, Sizes(parts), 0, 0);
        var at = s.PartStart(part) + LineIndex.ByteOfLine(parts[part], line);
        var from = StartAtOrBefore(s, Math.Max(0, at - bytes / 2));
        return s with { From = from, To = EndAfter(s, from, bytes) };
    }

    /// <summary>The window a <see cref="Step"/> toward the start: it gains the lines before it and gives up as many at its end.</summary>
    public static LogSpan Earlier(LogSpan s, long bytes)
    {
        s = s with { Sizes = Sizes(s.Parts) };
        if (s.AtStart)
        {
            return s;
        }

        var from = StartAtOrBefore(s, Math.Max(0, s.From - Step(bytes)));
        if (from >= s.From)
        {
            from = StartAtOrBefore(s, s.From - 1);   // a line longer than a step
        }

        var to = Math.Min(s.To, StartAtOrBefore(s, from + bytes));
        return s with { From = from, To = to > from ? to : StartAtOrAfter(s, from + 1) };
    }

    /// <summary>The window a <see cref="Step"/> toward the end; at the end of the log it is <see cref="End"/>.</summary>
    public static LogSpan Later(LogSpan s, long bytes)
    {
        s = s with { Sizes = Sizes(s.Parts) };
        if (s.AtEnd)
        {
            return s;
        }

        var to = StartAtOrAfter(s, Math.Min(s.Total, s.To + Step(bytes)));
        if (to <= s.To)
        {
            to = StartAtOrAfter(s, s.To + 1);
        }

        var from = Math.Max(s.From, StartAtOrAfter(s, Math.Max(0, to - bytes)));
        return s with { From = from < to ? from : s.From, To = to };
    }

    /// <summary>The window from <paramref name="from"/>, as long as <paramref name="bytes"/> allows (one line at least).</summary>
    private static long EndAfter(LogSpan s, long from, long bytes)
    {
        var to = StartAtOrBefore(s, Math.Min(s.Total, from + bytes));
        return to > from ? to : StartAtOrAfter(s, Math.Min(s.Total, from + 1));
    }

    private static List<long> Sizes(IReadOnlyList<string> parts) => [.. parts.Select(p => File.Exists(p) ? new FileInfo(p).Length : 0)];

    /// <summary>The start of the line that holds byte <paramref name="at"/>, or the start of its part.</summary>
    internal static long StartAtOrBefore(LogSpan s, long at)
    {
        if (at <= 0)
        {
            return 0;
        }

        var part = s.PartOf(at);
        var start = s.PartStart(part);
        if (at >= s.Total)
        {
            return s.Total;
        }

        return start + Back(s.Parts[part], at - start);
    }

    /// <summary>The first line start at or after byte <paramref name="at"/> (the start of the next part, or the end, when the line runs to the end of its part).</summary>
    internal static long StartAtOrAfter(LogSpan s, long at)
    {
        if (at <= 0)
        {
            return 0;
        }

        if (at >= s.Total)
        {
            return s.Total;
        }

        var part = s.PartOf(at);
        var start = s.PartStart(part);
        return at == start ? at : start + Forward(s.Parts[part], at - start, s.Sizes[part]);
    }

    /// <summary>In one file: the byte after the last line break before <paramref name="at"/> (0 when there is none).</summary>
    private static long Back(string path, long at)
    {
        using var fs = Open(path);
        var buf = ArrayPool<byte>.Shared.Rent(Block);
        try
        {
            for (var end = Math.Min(at, fs.Length); end > 0;)
            {
                var from = Math.Max(0, end - Block);
                fs.Seek(from, SeekOrigin.Begin);
                var n = Fill(fs, buf, (int)(end - from));
                var nl = buf.AsSpan(0, n).LastIndexOf((byte)'\n');
                if (nl >= 0)
                {
                    return from + nl + 1;
                }

                end = from;
            }

            return 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>In one file: the byte after the first line break at or after <paramref name="at"/> - 1 (the size when there is none).</summary>
    private static long Forward(string path, long at, long size)
    {
        using var fs = Open(path);
        var buf = ArrayPool<byte>.Shared.Rent(Block);
        try
        {
            for (var from = at - 1; from < size;)
            {
                fs.Seek(from, SeekOrigin.Begin);
                var n = Fill(fs, buf, (int)Math.Min(Block, size - from));
                if (n <= 0)
                {
                    break;
                }

                var nl = buf.AsSpan(0, n).IndexOf((byte)'\n');
                if (nl >= 0)
                {
                    return from + nl + 1;
                }

                from += n;
            }

            return size;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static int Fill(FileStream fs, byte[] buf, int count)
    {
        int n = 0, r;
        while (n < count && (r = fs.Read(buf, n, count - n)) > 0)
        {
            n += r;
        }

        return n;
    }

    internal static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
}
