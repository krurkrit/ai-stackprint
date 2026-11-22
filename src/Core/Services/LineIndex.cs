// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Text;

namespace AiStackprint.Services;

/// <summary>
/// Where the lines of a large log file start: the byte of every <see cref="Every"/>th line (lines end at \n, as search counts them), kept for the
/// last file asked about. Showing the lines around a match or counting the lines before a byte then reads from the nearest mark only, and counts
/// bytes without decoding text (a match near the end of a 120 MB file made a string of every line before it). A file that grew keeps its marks
/// and adds the new ones (logs are only appended to); one that shrank or changed at the same size is read again.
/// </summary>
public static class LineIndex
{
    internal const int Every = 4096;
    private const int Block = 64 * 1024;

    private sealed record Marks(string Path, long Size, DateTime Time, List<long> Starts, long Lines);   // Starts[k]: the byte line k·Every+1 starts at; Lines: \n seen

    private static readonly Lock _lock = new();
    private static Marks? _last;

    /// <summary>Lines <c>around - before</c> to <c>around + after</c> of a file (1-based, joined with \n), and the number of the first.</summary>
    public static (string Text, long First) Window(string path, long around, int before, int after)
    {
        long first = Math.Max(1, around - before), last = around + after;
        using var fs = Open(path);
        var (at, n) = Forward(fs, MarkBefore(fs, path, first), first, long.MaxValue);
        if (n < first)
        {
            return ("", first);   // the file has fewer lines
        }

        fs.Seek(at, SeekOrigin.Begin);
        using var r = new StreamReader(fs, Encoding.UTF8, at == 0, Block, true);
        var sb = new StringBuilder();
        var buf = ArrayPool<char>.Shared.Rent(Block);
        try
        {
            for (int c; n <= last && (c = r.Read(buf, 0, Block)) > 0;)
            {
                var span = buf.AsSpan(0, c);
                while (n <= last)
                {
                    var nl = span.IndexOf('\n');
                    if (nl < 0)
                    {
                        sb.Append(span);
                        break;
                    }

                    sb.Append(span[..(nl + 1)]);
                    span = span[(nl + 1)..];
                    n++;
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }

        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append('\n');   // the last line of the file has no line break
        }

        return (sb.Replace("\r\n", "\n").ToString(), first);
    }

    /// <summary>The byte line <paramref name="line"/> (1-based) starts at; the size of the file when it has fewer lines.</summary>
    public static long ByteOfLine(string path, long line)
    {
        using var fs = Open(path);
        var (at, n) = Forward(fs, MarkBefore(fs, path, Math.Max(1, line)), Math.Max(1, line), long.MaxValue);
        return n < line ? fs.Length : at;
    }

    /// <summary>Number of line breaks before byte <paramref name="offset"/> (which file line a view that starts there starts at, less one).</summary>
    public static long LinesBefore(string path, long offset)
    {
        using var fs = Open(path);
        var m = MarksOf(fs, path);
        var i = m.Starts.BinarySearch(offset);
        var k = Math.Max(0, i >= 0 ? i : ~i - 1);   // the last mark at or before the byte
        return Forward(fs, (m.Starts[k], (long)k * Every + 1), long.MaxValue, offset).Line - 1;
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);

    /// <summary>The mark at or before line <paramref name="line"/>: its byte and line number.</summary>
    private static (long At, long Line) MarkBefore(FileStream fs, string path, long line)
    {
        var m = MarksOf(fs, path);
        var k = (int)Math.Min((line - 1) / Every, m.Starts.Count - 1);
        return (m.Starts[k], (long)k * Every + 1);
    }

    /// <summary>From a byte that starts a line, reads on until line <paramref name="line"/> starts or byte <paramref name="until"/>: the byte and line reached.</summary>
    private static (long At, long Line) Forward(FileStream fs, (long At, long Line) from, long line, long until)
    {
        var (at, n) = from;
        var buf = ArrayPool<byte>.Shared.Rent(Block);
        try
        {
            fs.Seek(at, SeekOrigin.Begin);
            while (n < line && at < until)
            {
                var r = fs.Read(buf, 0, (int)Math.Min(Block, until - at));
                if (r <= 0)
                {
                    break;
                }

                var span = buf.AsSpan(0, r);
                for (int i = 0, nl; n < line && (nl = span[i..].IndexOf((byte)'\n')) >= 0;)
                {
                    i += nl + 1;
                    if (++n == line)
                    {
                        return (at + i, n);
                    }
                }

                at += r;
            }

            return (at, n);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static Marks MarksOf(FileStream fs, string path)
    {
        var size = fs.Length;
        var time = File.GetLastWriteTimeUtc(path);
        lock (_lock)
        {
            if (_last is { } m && m.Path == path && m.Size == size && m.Time == time)
            {
                return m;
            }

            var grew = _last is { } g && g.Path == path && g.Size < size;
            _last = Extend(fs, grew ? _last! : new Marks(path, 0, time, [0], 0), size, time);
            return _last;
        }
    }

    /// <summary>Adds the marks of the bytes after <see cref="Marks.Size"/>: reads only what the file grew by.</summary>
    private static Marks Extend(FileStream fs, Marks m, long size, DateTime time)
    {
        var starts = new List<long>(m.Starts);
        var lines = m.Lines;
        var buf = ArrayPool<byte>.Shared.Rent(Block);
        try
        {
            fs.Seek(m.Size, SeekOrigin.Begin);
            for (long at = m.Size; at < size;)
            {
                var r = fs.Read(buf, 0, (int)Math.Min(Block, size - at));
                if (r <= 0)
                {
                    break;
                }

                var span = buf.AsSpan(0, r);
                for (int i = 0, nl; (nl = span[i..].IndexOf((byte)'\n')) >= 0;)
                {
                    i += nl + 1;
                    if (++lines % Every == 0)
                    {
                        starts.Add(at + i);   // the line after this break is line lines+1 = k·Every+1
                    }
                }

                at += r;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        return new Marks(m.Path, size, time, starts, lines);
    }
}
