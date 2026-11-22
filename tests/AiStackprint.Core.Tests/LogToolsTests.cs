// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class LogToolsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private LogFile Log(string name, string text, DateTime? when = null, string? format = null)
    {
        var p = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        if (when != null)
        {
            File.SetLastWriteTime(p, when.Value);
        }

        var fi = new FileInfo(p);
        return new LogFile("app", "Logs", p, fi.Length, fi.LastWriteTime, null, format);
    }

    [Fact]
    public void Search_finds_matches_with_line_numbers_case_and_regex()
    {
        var f = Log("a.log", "ok\nERROR one\nfine\nerror two\n");
        var hits = LogSearch.File(f.Path, LogSearch.Pattern("error", false, false), LogSearch.MaxHits, TestContext.Current.CancellationToken);
        Assert.Equal([2, 4], hits.Select(h => h.Line));
        Assert.Equal(0, hits[0].Column);
        Assert.Single(LogSearch.File(f.Path, LogSearch.Pattern("error", true, false), LogSearch.MaxHits, TestContext.Current.CancellationToken));
        Assert.Equal(2, LogSearch.File(f.Path, LogSearch.Pattern(@"(one|two)$", false, true), LogSearch.MaxHits, TestContext.Current.CancellationToken).Count);
        Assert.ThrowsAny<ArgumentException>(() => LogSearch.Pattern("(", false, true));
    }

    [Fact]
    public void Window_and_lines_before_locate_file_lines()
    {
        var f = Log("w.log", string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}")) + "\n");
        var (text, first) = LogSearch.Window(f.Path, 50, before: 5, after: 2);
        Assert.Equal(45, first);
        Assert.StartsWith("line 45\n", text);
        Assert.EndsWith("line 52\n", text);
        Assert.Equal(3, LogSearch.LinesBefore(f.Path, "line 1\nline 2\nline 3\n".Length));
    }

    [Fact]
    public void Trash_moves_only_allowed_files_into_freedesktop_trash()
    {
        var keep = Log("keep.log", "x");
        var del = Log("del.log", "y");
        var trash = Path.Combine(_tmp, "Trash");
        var (moved, failed) = LogTrash.Delete([del.Path, keep.Path], [del], trash);
        Assert.Equal(new[] { del.Path }, moved);
        Assert.Equal(keep.Path, Assert.Single(failed).Path);
        Assert.True(File.Exists(keep.Path));
        Assert.False(File.Exists(del.Path));
        Assert.True(File.Exists(Path.Combine(trash, "files", "del.log")));
        Assert.Contains("Path=", File.ReadAllText(Path.Combine(trash, "info", "del.log.trashinfo")));
    }

    [Fact]
    public void A_second_file_of_the_same_name_gets_a_number_in_the_trash_and_a_missing_one_says_why()
    {
        var trash = Path.Combine(_tmp, "Trash");
        var first = Log("a/same.log", "1");
        LogTrash.Delete([first.Path], [first], trash);
        var second = Log("b/same.log", "2");
        LogTrash.Delete([second.Path], [second], trash);
        Assert.Equal("2", File.ReadAllText(Path.Combine(trash, "files", "same.2.log")));
        var gone = Log("gone.log", "x");
        File.Delete(gone.Path);
        Assert.Single(LogTrash.Delete([gone.Path], [gone], trash).Failed);
    }

    [Fact]
    public void Older_than_and_active_use_modified_time_and_now_by_default()
    {
        var recent = new LogFile("a", "Logs", "/x/new.log", 1, DateTime.Now, null);
        Assert.True(LogTrash.IsActive(recent));
        Assert.Empty(LogTrash.OlderThan([recent], 1));
    }

    [Fact]
    public void Older_than_and_active_use_modified_time()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Local);
        var old = new LogFile("a", "Logs", "/x/old.log", 1, now.AddDays(-40), null);
        var fresh = new LogFile("a", "Logs", "/x/new.log", 1, now.AddSeconds(-10), null);
        Assert.Equal(new[] { old }, LogTrash.OlderThan([old, fresh], 30, now));
        Assert.True(LogTrash.IsActive(fresh, now));
        Assert.False(LogTrash.IsActive(old, now));
    }

    [Fact]
    public void Summary_counts_files_errors_and_sessions_within_period()
    {
        var now = DateTime.Now;
        var plain = Log("app.log", "INFO start\nERROR boom\nWARN slow\n", now.AddDays(-1));
        var stale = Log("old.log", "ERROR ancient\n", now.AddDays(-60));
        var session = Log("s.jsonl",
            """{"type":"user","message":{"content":"hi"}}""" + "\n" +
            """{"type":"assistant","message":{"model":"m1","usage":{"input_tokens":10,"output_tokens":3},"content":[{"type":"tool_use","name":"Bash","input":{"command":"ls"}}]}}""" + "\n",
            now.AddHours(-2), "claude-session");
        var s = LogSummary.Compute([plain, stale, session], now.AddDays(-7), TestContext.Current.CancellationToken);
        Assert.Equal(2, s.Files);
        Assert.Equal(1, s.Errors);
        Assert.Equal(1, s.Warnings);
        Assert.Equal(1, s.Sessions);
        Assert.Equal(1, s.Prompts);
        Assert.Equal(1, s.ToolCalls);
        Assert.Equal(("Bash", 1), Assert.Single(s.TopTools));
        Assert.Equal(10, s.InputTokens);
        Assert.Equal(new[] { "m1" }, s.Models);
        Assert.Equal(3, LogSummary.Compute([plain, stale, session], null, TestContext.Current.CancellationToken).Files);
    }

    [Fact]
    public void Folder_watcher_batches_changes_and_flags_new_files()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp, "logs")).FullName;
        var file = Path.Combine(dir, "current.log");
        File.WriteAllText(file, "a");
        var batches = new System.Collections.Concurrent.ConcurrentQueue<LogFolderWatcher.Batch>();
        using var w = new LogFolderWatcher([(dir, "*.log", false)], file, TimeSpan.FromMilliseconds(300)) { Quiet = TimeSpan.FromMilliseconds(150) };
        w.Changed += batches.Enqueue;
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");   // not a log file: ignored
        Thread.Sleep(600);
        Assert.Empty(batches);

        File.AppendAllText(file, "b");
        File.AppendAllText(file, "c");
        Wait(() => !batches.IsEmpty);
        Thread.Sleep(500);   // events inside the quiet period are one batch
        Assert.InRange(batches.Count, 1, 2);
        Assert.Contains(batches, b => b.Paths.Contains(file, StringComparer.OrdinalIgnoreCase));
        Assert.All(batches, b => Assert.False(b.Structure));

        batches.Clear();
        var added = Path.Combine(dir, "next.log");
        File.WriteAllText(added, "new");
        Wait(() => batches.Any(b => b.Structure));
        Assert.Contains(batches, b => b.Structure && b.Paths.Contains(added, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Folder_watcher_reports_deleted_and_renamed_files_as_a_change_of_the_list()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp, "watched")).FullName;
        var file = Path.Combine(dir, "a.log");
        File.WriteAllText(file, "a");
        var batches = new System.Collections.Concurrent.ConcurrentQueue<LogFolderWatcher.Batch>();
        using var w = new LogFolderWatcher([(dir, "*.log", false), (Path.Combine(_tmp, "missing"), "*.log", false)], null) { Quiet = TimeSpan.FromMilliseconds(100) };
        w.Changed += batches.Enqueue;
        File.Move(file, Path.Combine(dir, "b.log"));
        Wait(() => batches.Any(b => b.Structure));
        Assert.Contains(batches, b => b.Structure);
        batches.Clear();
        File.Delete(Path.Combine(dir, "b.log"));
        Wait(() => batches.Any(b => b.Structure));
        Assert.Contains(batches, b => b.Structure);
    }

    [Fact]
    public void Without_a_folder_to_watch_the_newest_file_is_polled_and_its_removal_changes_the_list()
    {
        var file = Path.Combine(_tmp, "polled.log");
        File.WriteAllText(file, "a");
        var batches = new System.Collections.Concurrent.ConcurrentQueue<LogFolderWatcher.Batch>();
        using var w = new LogFolderWatcher([(Path.Combine(_tmp, "missing"), "*.log", false)], file, TimeSpan.FromMilliseconds(100)) { Quiet = TimeSpan.FromMilliseconds(50) };
        w.Changed += batches.Enqueue;
        File.AppendAllText(file, "more");
        Wait(() => batches.Any(b => !b.Structure));
        Assert.Contains(batches, b => !b.Structure && b.Paths.Contains(file));
        File.Delete(file);
        Wait(() => batches.Any(b => b.Structure));
        Assert.Contains(batches, b => b.Structure);
    }

    [Fact]
    public void Roots_and_restat_follow_the_log_sources()
    {
        var f = Log("sessions/p1/s.jsonl", "{}\n");
        var def = new AppDefinition
        {
            Id = "app",
            Logs = { new LogSource
            {
                Path = Path.Combine(_tmp, "sessions"),
                Pattern = "*.jsonl",
                Depth = 2
            }, new LogSource { Path = Path.Combine(_tmp, "one.log") } }
        };
        File.WriteAllText(Path.Combine(_tmp, "one.log"), "x");
        var roots = LogReader.Roots(def, []);
        Assert.Contains((Path.Combine(_tmp, "sessions"), "*.jsonl", true), roots);
        Assert.Contains((_tmp, "one.log", false), roots);
        File.AppendAllText(f.Path, "{}\n");
        Assert.Equal(6, LogReader.Restat(f)!.Size);
        File.Delete(f.Path);
        Assert.Null(LogReader.Restat(f));
    }

    private static void Wait(Func<bool> done)
    {
        var until = DateTime.Now.AddSeconds(5);
        while (!done() && DateTime.Now < until)
        {
            Thread.Sleep(50);
        }
    }
}
