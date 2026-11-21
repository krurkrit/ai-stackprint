// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>What was worked out from a log file is kept on disk, so the next start reads only the files that changed.</summary>
public sealed class FileMemoTests : IDisposable
{
    private sealed record Counts(int N);

    private readonly string _home = Path.Combine(Path.GetTempPath(), "sp-memo-" + Guid.NewGuid().ToString("N"));
    private readonly string _log;

    public FileMemoTests()
    {
        Directory.CreateDirectory(_home);
        _log = Path.Combine(_home, "a.log");
        File.WriteAllText(_log, "x");
        PathUtil.UseHome(_home);
    }

    public void Dispose()
    {
        PathUtil.UseHome(TestHome.Root);
        Directory.Delete(_home, true);
    }

    private LogFile File1() => new("app", "a", _log, new FileInfo(_log).Length, File.GetLastWriteTimeUtc(_log), null, "text");

    [Fact]
    public void A_saved_result_is_used_by_the_next_start_until_the_file_changes()
    {
        var made = 0;
        var first = new FileMemo<Counts>("memo-test");
        Assert.Equal(7, first.GetOrAdd(File1(), "v1", () => { made++; return new Counts(7); }).N);
        first.Flush();

        var next = new FileMemo<Counts>("memo-test");   // a new start: nothing in memory, the file on disk
        Assert.Equal(7, next.GetOrAdd(File1(), "v1", () => { made++; return new Counts(99); }).N);
        Assert.Equal(1, made);

        Assert.Equal(99, next.GetOrAdd(File1(), "v2", () => { made++; return new Counts(99); }).N);   // another version: worked out again
        File.AppendAllText(_log, "more");
        Assert.Equal(5, next.GetOrAdd(File1(), "v2", () => { made++; return new Counts(5); }).N);   // the file changed
        Assert.Equal(3, made);
    }

    [Fact]
    public void A_damaged_cache_file_is_worked_out_again_and_a_deleted_file_is_dropped()
    {
        Directory.CreateDirectory(DataFolder.Cache);
        File.WriteAllText(Path.Combine(DataFolder.Cache, "memo-bad.json"), "{ not json");
        var memo = new FileMemo<Counts>("memo-bad");
        Assert.Equal(1, memo.GetOrAdd(File1(), "v", () => new Counts(1)).N);
        var gone = Path.Combine(_home, "gone.log");
        File.WriteAllText(gone, "y");
        memo.GetOrAdd(File1() with { Path = gone }, "v", () => new Counts(2));
        File.Delete(gone);
        memo.Flush();
        Assert.DoesNotContain("gone.log", File.ReadAllText(Path.Combine(DataFolder.Cache, "memo-bad.json")));
        Assert.Contains("a.log", File.ReadAllText(Path.Combine(DataFolder.Cache, "memo-bad.json")));
    }
}
