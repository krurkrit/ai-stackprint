// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Stars and notes on the steps of a log file: kept by file and line.</summary>
public sealed class BookmarksTests
{
    [Fact]
    public void A_starred_step_is_found_by_file_and_line_and_the_path_ignores_case()
    {
        var s = new UiState();
        Bookmarks.Set(s, @"C:\logs\a.jsonl", 12, "fix it", true, null);
        Assert.NotNull(Bookmarks.Find(s, @"c:\LOGS\a.jsonl", 12));
        Assert.Null(Bookmarks.Find(s, @"C:\logs\a.jsonl", 13));
        Assert.Null(Bookmarks.Find(s, @"C:\logs\b.jsonl", 12));
    }

    [Fact]
    public void A_note_is_kept_without_a_star_and_a_step_with_neither_is_dropped()
    {
        var s = new UiState();
        Bookmarks.Set(s, "a", 1, "p", false, "  check this later  ");
        Assert.Equal("check this later", Bookmarks.Find(s, "a", 1)!.Note);
        Assert.False(Bookmarks.Find(s, "a", 1)!.Starred);
        Bookmarks.Set(s, "a", 1, "p", false, "   ");
        Assert.Empty(s.Bookmarks);
    }

    [Fact]
    public void Setting_a_step_again_replaces_it_and_a_file_lists_its_bookmarks_in_file_order()
    {
        var s = new UiState();
        Bookmarks.Set(s, "a", 30, "later", true, null);
        Bookmarks.Set(s, "a", 5, "first", true, "n1");
        Bookmarks.Set(s, "b", 1, "other file", true, null);
        Bookmarks.Set(s, "a", 5, "first", true, "n2");
        Assert.Equal([5L, 30L], Bookmarks.For(s, "a").Select(b => b.Line));
        Assert.Equal("n2", Bookmarks.Find(s, "a", 5)!.Note);
        Assert.Equal(3, s.Bookmarks.Count);
    }

    [Fact]
    public void The_oldest_go_when_there_are_too_many_and_a_long_preview_is_cut()
    {
        var s = new UiState();
        for (var i = 1; i <= Bookmarks.Max + 3; i++)
        {
            Bookmarks.Set(s, "a", i, new string('x', 500), true, null);
        }

        Assert.Equal(Bookmarks.Max, s.Bookmarks.Count);
        Assert.Null(Bookmarks.Find(s, "a", 1));
        Assert.NotNull(Bookmarks.Find(s, "a", Bookmarks.Max + 3));
        Assert.Equal(160, s.Bookmarks[0].Preview.Length);
    }
}
