using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Tests;

public class LogReaderTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    string Write(string rel, string text, DateTime when)
    {
        var f = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
        File.SetLastWriteTime(f, when);
        return f;
    }

    [Fact]
    public void Find_matches_pattern_and_depth_newest_first()
    {
        Write("logs/a.log", "a", new DateTime(2026, 1, 1));
        Write("logs/sub/b.log", "b", new DateTime(2026, 3, 1));
        Write("logs/sub/deep/c.log", "c", new DateTime(2026, 2, 1));
        Write("logs/notes.txt", "x", new DateTime(2026, 4, 1));
        var d = new AppDefinition { Id = "x", Logs = { new LogSource { Path = Path.Combine(_tmp, "logs"), Depth = 1 } } };
        var found = LogReader.Find(d, Array.Empty<string>());
        Assert.Equal(new[] { "b.log", "a.log" }, found.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Find_resolves_project_sources_per_project()
    {
        Write("p1/.app/logs/run.log", "r", DateTime.Now);
        var d = new AppDefinition { Id = "x", Logs = { new LogSource { Path = ".app/logs", Level = "Project" } } };
        var f = Assert.Single(LogReader.Find(d, new[] { Path.Combine(_tmp, "p1"), Path.Combine(_tmp, "p2") }));
        Assert.Equal(Path.Combine(_tmp, "p1"), f.Project);
    }

    [Fact]
    public void Tail_returns_whole_small_file()
    {
        var f = Write("s.log", "one\ntwo\n", DateTime.Now);
        var (text, truncated) = LogReader.Tail(f);
        Assert.Equal("one\ntwo\n", text);
        Assert.False(truncated);
    }

    [Fact]
    public void Tail_skips_partial_first_line_when_truncated()
    {
        var f = Write("big.log", "aaaaaaaaaa\nbbbb\ncccc\n", DateTime.Now);
        var (text, truncated) = LogReader.Tail(f, 12);
        Assert.Equal("bbbb\ncccc\n", text);
        Assert.True(truncated);
    }

    [Fact]
    public void Tail_reads_a_file_another_process_is_writing()
    {
        var f = Path.Combine(_tmp, "live.log");
        using var w = new FileStream(f, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        w.Write("hello\n"u8); w.Flush();
        Assert.Equal("hello\n", LogReader.Tail(f).Text);
    }

    [Fact]
    public void Filter_keeps_matching_lines_case_insensitive()
    {
        Assert.Equal("ERROR x\nan error", LogReader.Filter("ok\nERROR x\nan error\nfine", "error"));
        Assert.Equal("a\nb", LogReader.Filter("a\nb", ""));
    }
}
