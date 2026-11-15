// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>What changes a reply's price: long-context tiers, the 1-hour cache, fast mode, US-only processing and web searches.</summary>
public class UsagePricingTests
{
    private static readonly DateTime _t = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Local);

    private static ModelTable Table()
    {
        var t = new ModelTable();
        t.Models["claude-opus-5-5"] = new ModelInfo
        {
            Provider = "anthropic",
            Price = new() { Input = 4, Output = 20, CacheRead = 0.2m, CacheWrite = 5, CacheWrite1h = 8 },
            Multipliers = new() { ["fast"] = 2, ["us"] = 1.1m },
            WebSearch = 0.01m,
        };
        t.Models["gpt-6.1-sol"] = new ModelInfo
        {
            Provider = "openai",
            Price = new() { Input = 2, Output = 10, CacheRead = 0.1m },
            Above = [new PriceTier { Tokens = 272_000, Input = 4, Output = 15 }],
        };
        return t;
    }

    private static UsageEvent E(string model, long input, long output = 0, long read = 0, long write = 0, long write1h = 0, bool fast = false, bool us = false, int web = 0) =>
        new(_t, model, "p", input, output, read, write, 0, false, null, write1h, fast, us, web);

    [Fact]
    public void Without_the_new_fields_the_cost_is_the_old_estimate()
    {
        var t = Table();
        Assert.Equal(ModelCatalog.Cost("claude-opus-5-5", 1_000_000, 100_000, 2_000_000, 100_000, t), ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000, 100_000, 2_000_000, 100_000), t));
        Assert.Equal(4m + 2m + 0.4m + 0.5m, ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000, 100_000, 2_000_000, 100_000), t));
    }

    [Fact]
    public void One_hour_cache_writes_use_their_own_price()
    {
        // 1M written: 400K to the 1-hour cache at 8, 600K to the short one at 5
        Assert.Equal(0.6m * 5 + 0.4m * 8, ModelCatalog.Cost(E("claude-opus-5-5", 0, write: 1_000_000, write1h: 400_000), Table()));
    }

    [Fact]
    public void A_request_over_the_tier_is_priced_at_the_tier_and_fields_it_lacks_use_the_base()
    {
        var t = Table();
        Assert.Equal(0, ModelCatalog.TierOf("gpt-6.1-sol", 272_000, t));
        Assert.Equal(272_000, ModelCatalog.TierOf("gpt-6.1-sol", 272_001, t));
        // 300K input of which 200K cached: input 100K at 4, cache read 200K at the base 0.1 (the tier has none), output 10K at 15
        Assert.Equal(0.4m + 0.02m + 0.15m, ModelCatalog.Cost(E("gpt-6.1-sol", 100_000, 10_000, 200_000), t));
        // 120K: under the tier, base prices (input 100K at 2, cache read 20K at 0.1, output 10K at 10)
        Assert.Equal(0.2m + 0.002m + 0.1m, ModelCatalog.Cost(E("gpt-6.1-sol", 100_000, 10_000, 20_000), t));
    }

    [Fact]
    public void Fast_mode_and_US_only_multiply_and_web_searches_add()
    {
        var t = Table();
        var plain = ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000), t)!.Value;
        Assert.Equal(plain * 2, ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000, fast: true), t));
        Assert.Equal(plain * 2 * 1.1m, ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000, fast: true, us: true), t));
        Assert.Equal(plain + 0.03m, ModelCatalog.Cost(E("claude-opus-5-5", 1_000_000, web: 3), t));
        Assert.Equal(0.03m, ModelCatalog.SearchCost("claude-opus-5-5", 3, t));
        Assert.Equal(ModelCatalog.Cost(E("gpt-6.1-sol", 1_000), t), ModelCatalog.Cost(E("gpt-6.1-sol", 1_000, fast: true, us: true), t));   // no multipliers: none applied
    }

    [Fact]
    public void Buckets_split_by_tier_speed_and_region_and_cost_the_same_as_their_replies()
    {
        var t = Table();
        UsageEvent[] events =
        [
            E("gpt-6.1-sol", 100_000, 1_000), E("gpt-6.1-sol", 300_000, 1_000), E("gpt-6.1-sol", 280_000, 1_000),
            E("claude-opus-5-5", 1_000, 10, write: 500, write1h: 300), E("claude-opus-5-5", 1_000, 10, fast: true, web: 2), E("claude-opus-5-5", 1_000, 10, us: true),
        ];
        var buckets = UsageStats.Bucket(events, t);
        Assert.Equal(2, buckets.Count(b => b.Model == "gpt-6.1-sol"));
        Assert.Equal(2, buckets.Single(b => b.Model == "gpt-6.1-sol" && b.Tier == 272_000).Messages);
        Assert.Equal(3, buckets.Count(b => b.Model == "claude-opus-5-5"));
        Assert.Equal(300, buckets.Sum(b => b.CacheWrite1h));
        Assert.Equal(2, buckets.Sum(b => b.WebSearches));
        Assert.Equal(events.Sum(e => ModelCatalog.Cost(e, t)!.Value), buckets.Sum(b => ModelCatalog.Cost(b, t)!.Value));
    }

    [Fact]
    public void Claude_logs_give_the_cache_kind_speed_region_and_searches()
    {
        string[] lines =
        [
            """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","cwd":"C:\\Work\\a","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":20,"cache_read_input_tokens":0,"cache_creation_input_tokens":300,"cache_creation":{"ephemeral_5m_input_tokens":100,"ephemeral_1h_input_tokens":200},"speed":"fast","inference_geo":"us","server_tool_use":{"web_search_requests":2}}}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:01:00Z","cwd":"C:\\Work\\a","requestId":"r2","message":{"id":"m2","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":20,"speed":"standard","inference_geo":"global"}}}""",
        ];
        var events = new ClaudeSessionParser().Usage(lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l))).ToList();
        Assert.Equal((200L, true, true, 2), (events[0].CacheWrite1h, events[0].Fast, events[0].UsOnly, events[0].WebSearches));
        Assert.Equal((0L, false, false, 0), (events[1].CacheWrite1h, events[1].Fast, events[1].UsOnly, events[1].WebSearches));
    }
}
