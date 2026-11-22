// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The marks of a long file give the same lines and counts as reading it from the start.</summary>
public sealed class LineIndexTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("sp-lines-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private const int Lines = LineIndex.Every * 2 + 100;

    private string Write(string name, Func<int, string> line, string eol = "\n", bool bom = false)
    {
        var p = Path.Combine(_tmp, name);
        var text = string.Concat(Enumerable.Range(1, Lines).Select(n => line(n) + eol));
        File.WriteAllText(p, text, new UTF8Encoding(bom));
        return p;
    }

    /// <summary>What reading every line from the start gives: lines from..to (1-based) joined with \n.</summary>
    private static string Expected(string path, long from, long to) =>
        string.Concat(File.ReadAllText(path).Split('\n').SkipLast(1).Skip((int)from - 1).Take((int)(to - from + 1)).Select(l => l.TrimEnd('\r') + "\n"));

    [Theory]
    [InlineData(1)]
    [InlineData(LineIndex.Every)]
    [InlineData(LineIndex.Every + 1)]
    [InlineData(LineIndex.Every * 2 + 50)]
    [InlineData(Lines)]
    public void A_window_has_the_lines_around_a_line_anywhere_in_the_file(long around)
    {
        var p = Write("a.log", n => $"line {n} ü");
        var (text, first) = LineIndex.Window(p, around, 3, 2);
        Assert.Equal(Math.Max(1, around - 3), first);
        Assert.Equal(Expected(p, first, Math.Min(Lines, around + 2)), text);
    }

    [Fact]
    public void A_window_drops_the_byte_order_mark_and_carriage_returns_and_ends_the_last_line()
    {
        var p = Write("b.log", n => $"l{n}", "\r\n", bom: true);
        Assert.Equal("l1\nl2\n", LineIndex.Window(p, 1, 0, 1).Text);
        File.AppendAllText(p, "tail");
        Assert.Equal($"l{Lines}\ntail\n", LineIndex.Window(p, Lines + 1, 1, 5).Text);
        Assert.Equal("", LineIndex.Window(p, Lines + 10, 2, 2).Text);   // past the end
    }

    [Fact]
    public void Lines_before_a_byte_count_the_line_breaks_before_it_and_follow_a_growing_file()
    {
        var p = Write("c.log", n => new string('x', n % 7));
        var bytes = File.ReadAllBytes(p);
        foreach (var at in new[] { 0, 1, 5000, bytes.Length / 2, bytes.Length - 1, bytes.Length })
        {
            Assert.Equal(bytes.Take(at).Count(b => b == '\n'), LineIndex.LinesBefore(p, at));
        }

        File.AppendAllText(p, string.Concat(Enumerable.Repeat("grown\n", LineIndex.Every)));
        var size = new FileInfo(p).Length;
        Assert.Equal(Lines + LineIndex.Every, LineIndex.LinesBefore(p, size));
        Assert.Equal("grown\n", LineIndex.Window(p, Lines + LineIndex.Every, 0, 0).Text);
    }

    [Fact]
    public void A_file_written_again_is_read_again()
    {
        var p = Write("d.log", n => $"a{n}");
        Assert.Equal("a5000\n", LineIndex.Window(p, 5000, 0, 0).Text);
        File.WriteAllText(p, string.Concat(Enumerable.Range(1, Lines).Select(n => $"b{n}\n")));   // same size, another time
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("b5000\n", LineIndex.Window(p, 5000, 0, 0).Text);
    }
}
