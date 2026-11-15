// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.ModelData;

using Xunit;

namespace AiStackprint.ModelData.Tests;

/// <summary>The generator on a trimmed copy of LiteLLM's file (real field names and values).</summary>
public sealed class GeneratorTests
{
    private const string Sample = """
        {
          "sample_spec": { "litellm_provider": "one of https://docs.litellm.ai/docs/providers", "mode": "chat" },
          "claude-sonnet-4-5": {
            "litellm_provider": "anthropic", "mode": "chat", "max_input_tokens": 200000, "max_output_tokens": 64000,
            "input_cost_per_token": 3e-06, "output_cost_per_token": 1.5e-05, "cache_read_input_token_cost": 3e-07,
            "cache_creation_input_token_cost": 3.75e-06, "cache_creation_input_token_cost_above_1hr": 6e-06, "input_cost_per_token_batches": 1.5e-06,
            "search_context_cost_per_query": { "search_context_size_low": 0.01, "search_context_size_medium": 0.01, "search_context_size_high": 0.01 },
            "deprecation_date": "2026-11-30", "deprecation_source": "https://example.com/deprecations", "successor_model": "claude-sonnet-5-5",
            "source": "https://example.com/pricing", "supports_vision": true
          },
          "claude-sonnet-4-5-20250929": {
            "litellm_provider": "anthropic", "mode": "chat", "max_input_tokens": 200000, "max_output_tokens": 64000,
            "input_cost_per_token": 3e-06, "output_cost_per_token": 1.5e-05, "cache_read_input_token_cost": 3e-07,
            "cache_creation_input_token_cost": 3.75e-06, "cache_creation_input_token_cost_above_1hr": 6e-06,
            "search_context_cost_per_query": { "search_context_size_medium": 0.01 },
            "deprecation_date": "2026-11-30", "deprecation_source": "https://example.com/other", "successor_model": "claude-sonnet-5-5",
            "source": "https://example.com/old-pricing"
          },
          "claude-opus-5-5": {
            "litellm_provider": "anthropic", "mode": "chat", "max_input_tokens": 1000000, "max_output_tokens": 128000,
            "input_cost_per_token": 4e-06, "output_cost_per_token": 2e-05, "provider_specific_entry": { "us": 1.1, "fast": 2 }
          },
          "claude-opus-5-5-20260101": {
            "litellm_provider": "anthropic", "mode": "chat", "input_cost_per_token": 5e-06, "output_cost_per_token": 2e-05
          },
          "gpt-6.1-sol": {
            "litellm_provider": "openai", "mode": "responses", "max_input_tokens": 922000, "max_output_tokens": 128000,
            "input_cost_per_token": 2e-06, "output_cost_per_token": 1e-05, "cache_read_input_token_cost": 1e-07,
            "input_cost_per_token_above_272k_tokens": 4e-06, "output_cost_per_token_above_272k_tokens": 1.5e-05,
            "input_cost_per_token_above_272k_tokens_priority": 8e-06, "regional_processing_uplift_multiplier_us": 1.1
          },
          "gemini/gemini-2.5-pro": {
            "litellm_provider": "gemini", "mode": "chat", "max_input_tokens": 1048576, "input_cost_per_token": 1.25e-06, "output_cost_per_token": 1e-05,
            "input_cost_per_token_above_200k_tokens": 2.5e-06, "successor_model": "gemini/gemini-3.8-flash", "supported_output_modalities": ["text"]
          },
          "gemini/gemini-2.5-pro-preview-tts": {
            "litellm_provider": "gemini", "mode": "chat", "input_cost_per_token": 1e-06, "output_cost_per_token": 2e-05, "supported_output_modalities": ["audio"]
          },
          "vertex_ai/gemini-2.5-pro": { "litellm_provider": "vertex_ai-language-models", "mode": "chat", "input_cost_per_token": 1.25e-06, "output_cost_per_token": 1e-05 },
          "text-embedding-3-large": { "litellm_provider": "openai", "mode": "embedding", "input_cost_per_token": 1.3e-07 },
          "xai/grok-5": { "litellm_provider": "xai", "mode": "chat", "max_input_tokens": 2000000 },
          "deepseek/deepseek-chat": { "litellm_provider": "deepseek", "mode": "chat", "input_cost_per_token": 2.8e-07, "output_cost_per_token": 4.2e-07 },
          "mistral/codestral-latest": { "litellm_provider": "mistral", "mode": "chat", "input_cost_per_token": 3e-07, "output_cost_per_token": 9e-07 }
        }
        """;

    private static JsonObject Models() => Generator.Models(JsonNode.Parse(Sample)!.AsObject());

    [Fact]
    public void Keeps_chat_models_with_text_output_from_the_direct_providers()
    {
        Assert.Equal(["claude-opus-5-5", "claude-opus-5-5-20260101", "claude-sonnet-4-5", "codestral-latest", "deepseek-chat", "gemini-2.5-pro", "gpt-6.1-sol", "grok-5"],
            Models().Select(m => m.Key));   // sorted; no embedding, TTS, Vertex copy or sample_spec
    }

    [Fact]
    public void Prices_are_usd_per_million_tokens_with_camel_case_fields()
    {
        var m = Models()["claude-sonnet-4-5"]!;
        Assert.Equal("""{"input":3,"output":15,"cacheRead":0.3,"cacheWrite":3.75,"cacheWrite1h":6}""", m["price"]!.ToJsonString());
        Assert.Equal(200000, (long)m["contextWindow"]!);
        Assert.Equal(64000, (long)m["maxOutput"]!);
        Assert.Equal(0.01m, (decimal)m["webSearch"]!);
        Assert.Equal("2026-11-30", (string)m["retires"]!);
        Assert.Equal("claude-sonnet-5-5", (string)m["successor"]!);
        Assert.Null(m["supports_vision"]);
    }

    [Fact]
    public void A_dated_copy_is_dropped_only_when_its_numbers_are_the_same()
    {
        var models = Models();
        Assert.False(models.ContainsKey("claude-sonnet-4-5-20250929"));   // same numbers, other links
        Assert.True(models.ContainsKey("claude-opus-5-5-20260101"));      // another price
    }

    [Fact]
    public void The_provider_prefix_is_removed_also_from_the_successor()
    {
        var g = Models()["gemini-2.5-pro"]!;
        Assert.Equal("gemini-3.8-flash", (string)g["successor"]!);
        Assert.Equal("gemini", (string)g["provider"]!);
    }

    [Fact]
    public void Long_context_tiers_and_multipliers_are_kept_and_priority_prices_are_not()
    {
        var models = Models();
        Assert.Equal("""[{"tokens":272000,"input":4,"output":15}]""", models["gpt-6.1-sol"]!["above"]!.ToJsonString());
        Assert.Equal("""{"us":1.1}""", models["gpt-6.1-sol"]!["multipliers"]!.ToJsonString());
        Assert.Equal("""{"fast":2,"us":1.1}""", models["claude-opus-5-5"]!["multipliers"]!.ToJsonString());
    }

    [Fact]
    public void A_model_without_both_prices_has_no_price_rather_than_zero()
    {
        var grok = Models()["grok-5"]!;
        Assert.Null(grok["price"]);
        Assert.Equal(2000000, (long)grok["contextWindow"]!);
    }

    [Fact]
    public void The_checks_pass_on_good_data_and_the_same_data_keeps_the_old_date_and_commit()
    {
        var litellm = JsonNode.Parse(Sample)!.AsObject();
        var first = Generator.Build(litellm, "abc1234", "2026-11-15", null);
        Assert.Empty(Checks.Problems(first, null, Writer.Write(first).Length));

        var again = Generator.Build(litellm, "def5678", "2026-11-22", JsonNode.Parse(Writer.Write(first))!.AsObject());
        Assert.Equal("2026-11-15", (string)again["updated"]!);
        Assert.Equal("abc1234", (string)again["source"]!["commit"]!);
        Assert.Equal(Writer.Write(first), Writer.Write(again));
    }

    [Fact]
    public void The_checks_fail_on_a_missing_provider_a_big_drop_a_large_file_and_a_bad_value()
    {
        var current = Generator.Build(JsonNode.Parse(Sample)!.AsObject(), "abc1234", "2026-11-15", null);
        var small = (JsonObject)current.DeepClone();
        var models = (JsonObject)small["models"]!;
        foreach (var k in models.Select(m => m.Key).Where(k => k.StartsWith("claude", StringComparison.Ordinal)).ToList())
        {
            models.Remove(k);
        }

        models["gpt-6.1-sol"]!["price"]!["input"] = -1;
        var problems = Checks.Problems(small, current, Checks.MaxBytes + 1);
        Assert.Contains(problems, p => p.Contains("No anthropic models", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("fewer than", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("over the", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("`gpt-6.1-sol`: price", StringComparison.Ordinal));
    }

    [Fact]
    public void A_price_change_over_ten_times_is_reported()
    {
        var current = Generator.Build(JsonNode.Parse(Sample)!.AsObject(), "abc1234", "2026-11-15", null);
        var next = (JsonObject)current.DeepClone();
        next["models"]!["claude-opus-5-5"]!["price"]!["input"] = 4000;
        Assert.Equal(["`claude-opus-5-5` input: 4 → 4000"], Checks.BigChanges(next, current));
        Assert.Contains("Price changes over 10", Checks.Report(next, current), StringComparison.Ordinal);
    }

    [Fact]
    public void The_layout_is_stable_with_small_objects_on_one_line()
    {
        var file = Generator.Build(JsonNode.Parse(Sample)!.AsObject(), "abc1234", "2026-11-15", null);
        var text = Writer.Write(file);
        Assert.Contains("""      "price": { "input": 3, "output": 15, "cacheRead": 0.3, "cacheWrite": 3.75, "cacheWrite1h": 6 },""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(file, JsonNode.Parse(text)));
    }
}
