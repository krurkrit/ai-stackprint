// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace AiStackprint.Core.Tests;

public class UsageTests
{
    private static readonly string[] _claudeLines =
    [
        """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","cwd":"C:\\Work\\app-one","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":20,"cache_read_input_tokens":1000,"cache_creation_input_tokens":300},"content":[{"type":"text","text":"a"}]}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:01Z","cwd":"C:\\Work\\app-one","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":20,"cache_read_input_tokens":1000,"cache_creation_input_tokens":300},"content":[{"type":"tool_use","id":"t","name":"Bash","input":{"command":"ls"}}]}}""",
        """{"type":"assistant","isSidechain":true,"timestamp":"2026-11-11T05:01:00Z","cwd":"/home/x/app-one","message":{"id":"m2","model":"claude-sonnet-5-5","usage":{"input_tokens":1,"output_tokens":2},"content":[{"type":"text","text":"b"}]}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:02:00Z","message":{"id":"m3","model":"<synthetic>","usage":{"input_tokens":9,"output_tokens":9},"content":[]}}""",
        "not json"
    ];

    [Fact]
    public void Claude_usage_counts_a_streamed_message_once_and_reads_cache_tokens()
    {
        var events = LogParsers.Find("claude-session")!.Usage(_claudeLines).ToList();
        Assert.Equal(2, events.Count);
        var a = events[0];
        Assert.Equal(("claude-opus-5-5", "app-one", 10L, 20L, 1000L, 300L, false), (a.Model, a.Project, a.Input, a.Output, a.CacheRead, a.CacheWrite, a.SubAgent));
        Assert.True(events[1].SubAgent);
        Assert.Equal("app-one", events[1].Project);
    }

    [Fact]
    public void Claude_session_tokens_do_not_double_count_a_streamed_message()
    {
        var s = LogParsers.Find("claude-session")!.Parse(_claudeLines);
        Assert.Equal(20, s.InputTokens);    // m1 once (10) + m2 (1) + m3 (9)
        Assert.Equal(31, s.OutputTokens);   // 20 + 2 + 9
    }

    [Fact]
    public void Codex_usage_is_the_difference_between_running_totals_with_cached_input_split_out()
    {
        var lines = new[]
        {
            """{"timestamp":"2026-11-11T05:00:00Z","type":"session_meta","payload":{"id":"s","cwd":"C:\\Work\\app-two"}}""",
            """{"timestamp":"2026-11-11T05:00:01Z","type":"turn_context","payload":{"model":"gpt-x"}}""",
            """{"timestamp":"2026-11-11T05:00:02Z","type":"event_msg","payload":{"type":"token_count","info":null}}""",
            """{"timestamp":"2026-11-11T05:00:03Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":10,"reasoning_output_tokens":4}}}}""",
            """{"timestamp":"2026-11-11T05:00:04Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":10,"reasoning_output_tokens":4}}}}""",
            """{"timestamp":"2026-11-11T05:00:05Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":250,"cached_input_tokens":200,"output_tokens":30,"reasoning_output_tokens":9}}}}"""
        };
        var e = LogParsers.Find("codex-session")!.Usage(lines).ToList();
        Assert.Equal(2, e.Count);
        Assert.Equal((40L, 10L, 60L, 4L), (e[0].Input, e[0].Output, e[0].CacheRead, e[0].Reasoning));
        Assert.Equal((10L, 20L, 140L, 5L), (e[1].Input, e[1].Output, e[1].CacheRead, e[1].Reasoning));
        Assert.Equal(("gpt-x", "app-two"), (e[0].Model, e[0].Project));
    }

    [Fact]
    public void Pricing_uses_the_longest_matching_key_and_returns_null_without_a_price()
    {
        var table = new PriceFile
        {
            Models = { ["claude"] = new() { Input = 1 }, ["claude-opus"] = new() { Input = 10, Output = 100, CacheRead = 1, CacheWrite = 12 } }
        };
        var b = new UsageBucket(new DateOnly(2026, 11, 11), "claude-opus-5-5", "p", 1_000_000, 500_000, 2_000_000, 100_000, 0, 1, 0);
        Assert.Equal(10m + 50m + 2m + 1.2m, Pricing.Cost(b, table));
        Assert.Equal(1m, Pricing.Cost(b with { Model = "claude-haiku", Input = 1_000_000, Output = 0, CacheRead = 0, CacheWrite = 0 }, table));
        Assert.Null(Pricing.Cost(b with { Model = "other" }, table));
    }

    [Fact]
    public void Compute_buckets_per_day_and_respects_the_start_day()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "usage-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(dir, "s.jsonl");
            File.WriteAllLines(path, _claudeLines);
            var info = new FileInfo(path);
            var file = new LogFile("claude", "Sessions", path, info.Length, info.LastWriteTime, null, "claude-session");
            var all = UsageStats.Compute([file], DateTime.MinValue, TestContext.Current.CancellationToken);
            Assert.Equal(1, all.Sessions); Assert.Equal(1, all.Files);
            Assert.Equal(1330L, all.Buckets.Where(b => b.Model == "claude-opus-5-5").Sum(b => b.Input + b.Output + b.CacheRead + b.CacheWrite));
            Assert.Equal(2, all.Buckets.Sum(b => b.Messages));
            var future = UsageStats.Compute([file], DateTime.Now.AddDays(2), TestContext.Current.CancellationToken);
            Assert.Empty(future.Buckets);
            var none = UsageStats.Compute([file with { Format = null }], DateTime.MinValue, TestContext.Current.CancellationToken);
            Assert.Equal(0, none.Files);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OpenCode_usage_reads_messages_and_v2_sessions_with_reasoning_in_output_and_its_own_cost()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                var cmd = db.CreateCommand();
                cmd.CommandText = """
                    create table session (id text, parent_id text);
                    create table message (id text, session_id text, time_created integer, data text);
                    create table session_v2 (id text, parent_id text, directory text, model text, cost real, tokens_input integer, tokens_output integer, tokens_reasoning integer, tokens_cache_read integer, tokens_cache_write integer, time_updated integer);
                    insert into session values ('s1', null), ('s2', 's1');
                    insert into message values
                      ('m1', 's1', 1794300000000, '{"role":"assistant","modelID":"big-pickle","path":{"cwd":"C:\\Work\\app-one"},"cost":0,"tokens":{"input":6,"output":205,"reasoning":0,"cache":{"read":10,"write":15655}}}'),
                      ('m2', 's2', 1794300001000, '{"role":"assistant","modelID":"gpt-x","path":{"cwd":"/home/x/app-one"},"cost":0.25,"tokens":{"input":1,"output":2,"reasoning":3,"cache":{"read":0,"write":0}}}'),
                      ('m3', 's1', 1794300002000, '{"role":"user","time":{}}');
                    insert into session_v2 values
                      ('v1', null, 'C:/Workspace/dx1', '{"id":"qwen3.8-27b","providerID":"litellm"}', 0, 12931, 605, 4243, 0, 0, 1794307268567),
                      ('s1', null, 'C:/x', '{"id":"dup"}', 0, 9, 9, 0, 0, 0, 1794307268567);
                    """;
                cmd.ExecuteNonQuery();
            }
            var events = LogParsers.Find("opencode-db")!.UsageFile(path).ToList();
            Assert.Equal(3, events.Count);   // user message and the session already in the old table are skipped
            var a = events.Single(e => e.Model == "big-pickle");
            Assert.Equal(("app-one", 6L, 205L, 10L, 15655L, false, (decimal?)null), (a.Project, a.Input, a.Output, a.CacheRead, a.CacheWrite, a.SubAgent, a.Cost));   // cost 0 = no price in OpenCode
            var b = events.Single(e => e.Model == "gpt-x");
            Assert.Equal((5L, 3L, true, (decimal?)0.25m), (b.Output, b.Reasoning, b.SubAgent, b.Cost));
            var c = events.Single(e => e.Model == "qwen3.8-27b");
            Assert.Equal(("dx1", 12931L, 4848L, (decimal?)null), (c.Project, c.Input, c.Output, c.Cost));
        }
        finally { File.Delete(path); }
    }
}
