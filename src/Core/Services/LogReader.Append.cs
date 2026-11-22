// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

public static partial class LogReader
{
    /// <summary>What <see cref="CompleteLines"/> read: the lines (rows), the byte the next read starts at, and whether that byte is inside a line (its rest is dropped then).</summary>
    public readonly record struct Piece(List<ReadOnlyMemory<byte>> Rows, long End, bool InLine);

    /// <summary>
    /// The lines from byte <paramref name="from"/> to the end of the file, for reading a log in pieces while it is written. A last line without a line break is
    /// kept only when it is a whole JSON value (the writer may be half-way through it; it is read next time otherwise).
    /// </summary>
    /// <param name="dropFirst">The read starts inside a line (a file cut to its end, or a JSON line kept without its line break): the text up to the first line break is dropped.</param>
    public static Piece CompleteLines(string path, long from, bool dropFirst)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var size = fs.Length;
        if (size <= from)
        {
            return new([], from, dropFirst);
        }

        fs.Seek(from, SeekOrigin.Begin);
        var buf = new byte[size - from];
        int n = 0, r;
        while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0)
        {
            n += r;
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

        if (start < n && IsJson(buf.AsMemory(start, n - start)))
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
