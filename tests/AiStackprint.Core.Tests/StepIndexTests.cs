// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>A step is found by its file and line: the step that starts at or before the line, in the session file or in a sub-agent's file.</summary>
public sealed class StepIndexTests
{
    private static LogEntry Step(string text, long line, string? source = null) => new("assistant", text, null, source != null) { Line = line, Source = source };

    private static readonly StepIndex _index = new([Step("a", 1), Step("b", 5), Step("c", 5), Step("d", 9), Step("x", 2, "agent1"), Step("y", 7, "agent1"), new LogEntry("user", "no line", null, false) { Line = 0 }]);

    [Theory]
    [InlineData(1, "a")]
    [InlineData(4, "a")]   // inside the first step
    [InlineData(5, "b,c")]   // two steps start on this line
    [InlineData(8, "b,c")]
    [InlineData(500, "d")]
    public void A_line_finds_the_step_that_starts_at_or_before_it(long line, string expected) =>
        Assert.Equal(expected, string.Join(",", _index.Find(null, line).Select(e => e.Text)));

    [Fact]
    public void A_sub_agents_file_has_its_own_lines()
    {
        Assert.Equal("x", Assert.Single(_index.Find("agent1", 6)).Text);
        Assert.Equal("y", Assert.Single(_index.Find("agent1", 7)).Text);
        Assert.Empty(_index.Find("other", 1));
    }

    [Fact]
    public void A_line_older_than_every_step_finds_none_and_is_not_covered()
    {
        var older = new StepIndex([Step("m", 100)]);
        Assert.Empty(older.Find(null, 99));
        Assert.False(older.Covers(null, 99));
        Assert.True(older.Covers(null, 100));
        Assert.False(older.Covers("agent1", 100));
    }
}
