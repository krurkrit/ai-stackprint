// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>A long log is shown a window at a time: the window starts and ends at whole lines, moves a quarter at a time, and its steps keep the files' own lines.</summary>
public sealed class LogWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sp-window-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>100 lines of 9 bytes each ("line 001\n" … "line 100\n").</summary>
    private string Numbered(string name = "a.log", int count = 100, int first = 1)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, string.Concat(Enumerable.Range(first, count).Select(n => $"line {n:000}\n")));
        return path;
    }

    private static void Whole(LogSpan s)
    {
        Assert.Equal(0, s.From % 9);   // both ends at the start of a line
        Assert.True(s.To % 9 == 0 || s.To == s.Total);
        Assert.True(s.To > s.From);
    }

    [Fact]
    public void A_window_opens_on_the_end_and_moves_a_quarter_at_a_time_to_the_start_and_back()
    {
        var path = Numbered();
        var s = LogWindow.End([path], 90);
        Assert.Equal((810L, 900L), (s.From, s.To));   // the last 10 lines
        Assert.True(s.AtEnd);
        Assert.False(s.AtStart);
        var seen = 0;
        while (!s.AtStart)
        {
            var before = s;
            s = LogWindow.Earlier(s, 90);
            Whole(s);
            Assert.True(s.From < before.From);
            Assert.True(s.To - s.From <= 90);
            Assert.True(before.From - s.From <= LogWindow.Step(90) + 9);   // a quarter (to a whole line)
            Assert.True(s.To > before.From);   // the parts overlap: the view keeps the steps it had at its top
            seen++;
        }

        Assert.InRange(seen, 30, 45);
        Assert.Equal(0, s.From);
        Assert.Equal(0, LogWindow.Earlier(s, 90).From);   // at the start: it stays
        while (!s.AtEnd)
        {
            var before = s;
            s = LogWindow.Later(s, 90);
            Whole(s);
            Assert.True(s.To > before.To);
            Assert.True(s.From < before.To);
        }

        Assert.Equal(900, s.To);
    }

    [Fact]
    public void A_window_around_a_line_holds_it_and_its_text_starts_at_its_first_line()
    {
        var path = Numbered();
        var s = LogWindow.Around([path], 0, 50, 90);
        Whole(s);
        var (text, first) = LogWindow.Text(s);
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal($"line {first:000}", lines[0]);   // the first line's number is the file's
        Assert.Contains("line 050", lines);
        Assert.Equal((s.To - s.From) / 9, lines.Length);
        Assert.Equal(441, LineIndex.ByteOfLine(path, 50));
        Assert.Equal(900, LineIndex.ByteOfLine(path, 500));   // past the end: the size
    }

    [Fact]
    public void The_start_of_a_log_is_a_window_too_and_a_line_longer_than_a_window_is_read_whole()
    {
        var path = Path.Combine(_dir, "long.log");
        File.WriteAllText(path, "short\n" + new string('x', 300) + "\nend\n");
        var s = LogWindow.Start([path], 100);
        Assert.Equal((0L, 6L), (s.From, s.To));   // the first line: the next one does not fit
        var next = LogWindow.Later(s, 100);
        Assert.Equal(307, next.To);   // the long line, whole
        Assert.Equal(new string('x', 300), LogWindow.Text(next).Text.Split('\n')[^2]);
        var back = LogWindow.Earlier(LogWindow.End([path], 100), 100);
        Assert.True(back.From < 307);
    }

    [Fact]
    public void The_numbered_parts_of_a_text_log_are_read_as_one_log_oldest_first()
    {
        var older = Numbered("main1.log", 3, 1);
        var newer = Numbered("main.log", 3, 4);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        static LogFile F(string p, string? format = LineLogs.Format) => new("app", "Logs", p, new FileInfo(p).Length, File.GetLastWriteTime(p), null, format);
        var other = Numbered("mcp.log", 1);
        var parts = LogParts.Of(F(newer), [F(newer), F(other), F(older)]);
        Assert.Equal([older, newer], parts.Select(p => p.Path));   // mcp.log is another log
        Assert.Equal([newer], LogParts.Of(F(newer, "claude-session"), [F(newer), F(older)]).Select(p => p.Path));   // sessions are never parts
        Assert.Equal(LogParts.Key("main.log"), LogParts.Key("main.old.log"));
        Assert.Equal(LogParts.Key("main.log"), LogParts.Key("main.2.log"));
        Assert.Equal(LogParts.Key("app-2026-08-07.log"), LogParts.Key("app-2026-08-08.log"));

        File.WriteAllText(older, "2026-08-07 10:00:00 [info] one\n2026-08-07 10:00:01 [error] two\n");
        File.WriteAllText(newer, "2026-08-07 11:00:00 [info] three\n2026-08-07 11:00:01 [warn] four\n");
        var paths = parts.Select(p => p.Path).ToList();
        var end = LogWindow.End(paths, 1 << 20);
        Assert.True(end.AtStart);   // both fit
        var s = LogWindow.Parse(F(newer), end);
        Assert.Equal(["one", "two", "three", "four"], s.Entries.Select(e => e.Text));
        Assert.Equal([older, older, null, null], s.Entries.Select(e => e.Source));   // the other part's lines say which file they are in
        Assert.Equal([1L, 2, 1, 2], s.Entries.Select(e => e.Line));   // each file's own lines
        var tail = LogWindow.End(paths, 40);
        Assert.Equal(["four"], LogWindow.Parse(F(newer), tail).Entries.Select(e => e.Text));
        var earlier = LogWindow.Earlier(tail, 40);
        Assert.Equal(1, earlier.PartOf(earlier.To - 1));
        Assert.Contains("of", LogWindow.Where(earlier), StringComparison.Ordinal);
    }

    [Fact]
    public void A_window_of_a_session_has_its_lines_and_leaves_out_the_steps_before_it()
    {
        var path = Path.Combine(_dir, "s.jsonl");
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 200).Select(n =>
            "{\"type\":\"" + (n % 2 == 0 ? "user" : "assistant") + "\",\"timestamp\":\"2026-11-11T05:" + (n / 60).ToString("00") + ":" + (n % 60).ToString("00") + "Z\",\"message\":{\"role\":\""
            + (n % 2 == 0 ? "user" : "assistant") + "\",\"content\":\"step " + n + "\"}}\n")));
        var f = new LogFile("claude-code", "s", path, new FileInfo(path).Length, DateTime.Now, null, "claude-session");
        Assert.True(LogWindow.Fits(f.Format));
        Assert.False(LogWindow.Fits("opencode-db"));
        var s = LogWindow.Around([path], 0, 100, 4000);
        var session = LogWindow.Parse(f, s);
        Assert.NotEmpty(session.Entries);
        Assert.All(session.Entries, e => Assert.Equal("step " + (e.Line - 1), e.Text));   // step n is line n + 1 of the file
        Assert.Contains(session.Entries, e => e.Line == 100);
        Assert.Equal(LineIndex.LinesBefore(path, s.From) + 1, session.Entries[0].Line);   // nothing from before the window (it was read only to pair calls with results)
    }
}
