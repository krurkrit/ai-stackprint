// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class LogQueryTests
{
    private static LogQuery Q(string text, bool matchCase = false, bool regex = false, bool steps = false) => LogQuery.Parse(text, matchCase, regex, steps)!;

    [Fact]
    public void Words_match_separately_in_any_order_and_quotes_keep_a_phrase()
    {
        Assert.True(Q("refused connection").IsMatch("Error: connection refused"));
        Assert.False(Q("refused timeout").IsMatch("Error: connection refused"));
        Assert.True(Q("\"connection refused\"").IsMatch("Error: connection refused"));
        Assert.False(Q("\"refused connection\"").IsMatch("Error: connection refused"));
        Assert.False(Q("error", matchCase: true).IsMatch("Error: x"));
        Assert.Null(LogQuery.Parse("  ", false, false));
    }

    [Fact]
    public void Regex_is_one_pattern_and_an_invalid_one_throws()
    {
        Assert.True(Q("(one|two)$", regex: true).IsMatch("line two"));
        Assert.False(Q("two one", regex: true).IsMatch("one two"));   // not split into words
        Assert.ThrowsAny<ArgumentException>(() => LogQuery.Parse("(", false, true));
    }

    [Fact]
    public void Spans_mark_every_word_and_first_is_the_earliest_when_all_match()
    {
        var q = Q("bar foo");
        Assert.Equal([(0, 3), (4, 3), (12, 3)], q.Spans("foo bar baz foo"));
        Assert.Equal((0, 3), q.First("foo bar"));
        Assert.Null(q.First("foo only"));          // not every word
        Assert.Equal([(0, 3)], q.Spans("foo only"));   // still marked in a cut snippet
    }

    [Fact]
    public void Role_and_tool_filter_conversation_steps_and_are_text_in_the_raw_file()
    {
        var bash = new LogEntry("tool", "ls -la", null, false, "Bash");
        var said = new LogEntry("user", "please run ls", null, false);
        var q = Q("role:tool ls", steps: true);
        Assert.Equal(("tool", null), (q.Role, q.Tool));
        Assert.True(q.IsMatch(bash));
        Assert.False(q.IsMatch(said));
        Assert.True(Q("tool:bash", steps: true).IsMatch(bash));
        Assert.False(Q("tool:read", steps: true).IsMatch(bash));
        Assert.True(Q("role:", steps: true) is null);   // still typing

        Assert.True(Q("role:user").IsMatch("{\"x\":1} role:user"));   // raw file: searched as written
        Assert.True(Q("https://example.com").IsMatch("GET https://example.com/a"));
    }

    [Fact]
    public void File_search_reads_in_blocks_and_keeps_line_numbers()
    {
        var path = Path.GetTempFileName();
        try
        {
            var longLine = new string('x', 200_000) + " needle far";   // longer than one block
            File.WriteAllText(path, "first\r\nneedle here\r\n" + longLine + "\nno\nlast needle");   // no break after the last line
            var hits = LogSearch.File(path, LogSearch.Pattern("needle", false, false), LogSearch.MaxHits, TestContext.Current.CancellationToken);
            Assert.Equal([2L, 3L, 5L], hits.Select(h => h.Line));
            Assert.Equal(("needle here", 0, 6), (hits[0].Text, hits[0].Column, hits[0].Length));   // no \r
            Assert.Equal(200_001, hits[1].Column);
            Assert.Equal(2, LogSearch.File(path, LogSearch.Pattern("needle", false, false), 2, TestContext.Current.CancellationToken).Count);
        }
        finally { File.Delete(path); }
    }
}
