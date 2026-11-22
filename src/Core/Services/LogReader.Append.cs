// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

public static partial class LogReader
{
    /// <summary>What <see cref="CompleteLines(string, long, bool)"/> read: the lines (rows), the byte the next read starts at, and whether that byte is inside a line (its rest is dropped then).</summary>
    public readonly record struct Piece(List<ReadOnlyMemory<byte>> Rows, long End, bool InLine);

    /// <summary>
    /// The lines from byte <paramref name="from"/> to the end of the file, for reading a log in pieces while it is written. A last line without a line break is
    /// kept only when it is a whole JSON value (the writer may be half-way through it; it is read next time otherwise).
    /// </summary>
    /// <param name="dropFirst">The read starts inside a line (a file cut to its end, or a JSON line kept without its line break): the text up to the first line break is dropped.</param>
    public static Piece CompleteLines(string path, long from, bool dropFirst) => CompleteLines(path, from, dropFirst, long.MaxValue);

    /// <summary>Like the other overload, reading about <paramref name="maxBytes"/> at a time (a large file in batches): the lines end at the last line break read, and a line longer than that is read whole.</summary>
    public static Piece CompleteLines(string path, long from, bool dropFirst, long maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var size = fs.Length;
        if (size <= from)
        {
            return new([], from, dropFirst);
        }

        var want = Math.Min(size - from, maxBytes);
        byte[] buf;
        int n;
        bool atEnd;
        while (true)
        {
            fs.Seek(from, SeekOrigin.Begin);
            buf = new byte[want];
            n = 0;
            for (int r; n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0;)
            {
                n += r;
            }

            atEnd = from + n >= size;
            if (atEnd || Array.IndexOf(buf, (byte)'\n', 0, n) >= 0)
            {
                break;
            }

            want = Math.Min(size - from, want * 2);   // one line longer than a batch
        }

        var start = from == 0 && n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF ? 3 : 0;   // byte order mark
        if (dropFirst)
        {
            var nl = Array.IndexOf(buf, (byte)'\n', 0, n);
            if (nl < 0)
            {
                return new([], from + n, true);   // still the same line
            }

            start = nl + 1;
        }

        var lines = new List<ReadOnlyMemory<byte>>();
        int at;
        while ((at = Array.IndexOf(buf, (byte)'\n', start, n - start)) >= 0)
        {
            lines.Add(buf.AsMemory(start, at > start && buf[at - 1] == '\r' ? at - start - 1 : at - start));
            start = at + 1;
        }

        if (start < n && atEnd && IsJson(buf.AsMemory(start, n - start)))
        {
            lines.Add(buf.AsMemory(start, n - start));
            return new(lines, from + n, true);
        }

        return new(lines, from + start, false);
    }

    private static bool IsJson(ReadOnlyMemory<byte> text)
    {
        if (text.Span.IndexOfAnyExcept(" \t\r"u8) < 0)
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
