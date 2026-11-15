// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AiStackprint.ModelData;

/// <summary>
/// LiteLLM's model list → default/models.json: chat models with text output from the providers below, USD per 1M tokens, camelCase fields,
/// the provider prefix removed (<c>gemini/gemini-3.8-flash</c> → <c>gemini-3.8-flash</c>, as the apps log it), dated copies with the same numbers dropped.
/// </summary>
public static partial class Generator
{
    /// <summary>Direct providers kept (LiteLLM's <c>litellm_provider</c>); the schema lists the same plus <c>custom</c>.</summary>
    public static readonly string[] Providers = ["anthropic", "openai", "gemini", "xai", "deepseek", "mistral"];

    public const string SourceName = "LiteLLM model_prices_and_context_window.json";
    public const string SourceUrl = "https://github.com/BerriAI/litellm";

    private static readonly string[] _modes = ["chat", "responses"];
    private static readonly (string LiteLlm, string Ours)[] _prices =
    [
        ("input_cost_per_token", "input"), ("output_cost_per_token", "output"), ("cache_read_input_token_cost", "cacheRead"),
        ("cache_creation_input_token_cost", "cacheWrite"), ("cache_creation_input_token_cost_above_1hr", "cacheWrite1h"),
    ];

    /// <summary>The models part of the file, sorted by key.</summary>
    public static JsonObject Models(JsonObject litellm)
    {
        var found = new Dictionary<string, (JsonObject Model, bool Prefixed)>(StringComparer.Ordinal);
        foreach (var (key, node) in litellm)
        {
            if (node is not JsonObject e || Str(e, "litellm_provider") is not { } provider || !Keep(e, provider))
            {
                continue;
            }

            var name = Name(key, provider, out var prefixed);
            if (!KeyPattern().IsMatch(name) || (found.TryGetValue(name, out var had) && !had.Prefixed))
            {
                continue;   // a plain key wins over the same name with the provider prefix
            }

            found[name] = (Map(e, provider), prefixed);
        }

        var models = new JsonObject();
        foreach (var name in found.Keys.Order(StringComparer.Ordinal))
        {
            var m = found[name].Model;
            if (Dated().Match(name) is { Success: true } d && found.TryGetValue(d.Groups[1].Value, out var plain) && SameNumbers(m, plain.Model))
            {
                continue;   // claude-sonnet-4-5-20250929 is claude-sonnet-4-5: the longest-key lookup finds the plain one
            }

            models[name] = m;
        }
        return models;
    }

    /// <summary>The whole file. When the models are the same as in <paramref name="current"/>, its date and commit stay, so an unchanged week makes no change.</summary>
    public static JsonObject Build(JsonObject litellm, string commit, string date, JsonObject? current)
    {
        var models = Models(litellm);
        if (current?["models"] is JsonObject old && JsonNode.DeepEquals(old, models) && current["updated"] is JsonValue u && current["source"] is JsonObject s)
        {
            date = (string)u!;
            commit = (string?)s["commit"] ?? commit;
        }

        return new JsonObject
        {
            ["$schema"] = "./models.schema.json",
            ["version"] = 1,
            ["updated"] = date,
            ["source"] = new JsonObject { ["name"] = SourceName, ["url"] = SourceUrl, ["commit"] = commit, ["license"] = "MIT" },
            ["models"] = models,
        };
    }

    private static bool Keep(JsonObject e, string provider)
    {
        if (!Providers.Contains(provider) || !_modes.Contains(Str(e, "mode")))
        {
            return false;
        }

        if (e["supported_output_modalities"] is JsonArray outputs && !outputs.Any(o => (string?)o == "text"))
        {
            return false;   // marked chat, but answers with audio or images
        }

        return e.ContainsKey("input_cost_per_token") || e.ContainsKey("max_input_tokens");
    }

    private static string Name(string key, string provider, out bool prefixed)
    {
        var k = key.Trim().ToLowerInvariant();
        prefixed = k.StartsWith(provider + "/", StringComparison.Ordinal);
        return prefixed ? k[(provider.Length + 1)..] : k;
    }

    private static JsonObject Map(JsonObject e, string provider)
    {
        var m = new JsonObject { ["provider"] = provider };
        if (Int(e, "max_input_tokens") is { } ctx)
        {
            m["contextWindow"] = ctx;
        }

        if (Int(e, "max_output_tokens") is { } outTokens)
        {
            m["maxOutput"] = outTokens;
        }

        if (Usd(e, "input_cost_per_token") != null && Usd(e, "output_cost_per_token") != null)
        {
            var price = new JsonObject();
            foreach (var (theirs, ours) in _prices)
            {
                if (Usd(e, theirs) is { } v)
                {
                    price[ours] = v;
                }
            }
            m["price"] = price;
            if (Tiers(e) is { Count: > 0 } above)
            {
                m["above"] = above;
            }
        }

        if (Multipliers(e) is { Count: > 0 } mult)
        {
            m["multipliers"] = mult;
        }

        if (e["search_context_cost_per_query"] is JsonObject search && Num(search["search_context_size_medium"]) is { } perSearch)
        {
            m["webSearch"] = Round(perSearch);
        }

        Copy(e, "deprecation_date", m, "retires");
        if (Str(e, "successor_model") is { } next)
        {
            m["successor"] = Name(next, provider, out _);
        }

        Copy(e, "deprecation_source", m, "retiresSource");
        Copy(e, "source", m, "source");
        return m;
    }

    /// <summary><c>input_cost_per_token_above_272k_tokens</c> and the like, one tier per threshold, lowest first.</summary>
    private static JsonArray Tiers(JsonObject e)
    {
        var tiers = new SortedDictionary<long, JsonObject>();
        foreach (var (key, _) in e)
        {
            if (TierKey().Match(key) is not { Success: true } t || Usd(e, key) is not { } v)
            {
                continue;
            }

            var tokens = long.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture) * 1000;
            if (!tiers.TryGetValue(tokens, out var tier))
            {
                tiers[tokens] = tier = new JsonObject { ["tokens"] = tokens };
            }

            tier[_prices.First(p => p.LiteLlm == t.Groups[1].Value).Ours] = v;
        }

        var a = new JsonArray();
        foreach (var tier in tiers.Values)
        {
            var ordered = new JsonObject { ["tokens"] = tier["tokens"]!.DeepClone() };   // fields in the same order as price
            foreach (var (_, ours) in _prices)
            {
                if (tier[ours] is { } v)
                {
                    ordered[ours] = v.DeepClone();
                }
            }
            a.Add(ordered);
        }
        return a;
    }

    private static JsonObject Multipliers(JsonObject e)
    {
        var m = new JsonObject();
        if (e["provider_specific_entry"] is JsonObject p)
        {
            foreach (var k in new[] { "fast", "us" })
            {
                if (Num(p[k]) is { } v && v > 0)
                {
                    m[k] = Round(v);
                }
            }
        }

        if (!m.ContainsKey("us") && Num(e["regional_processing_uplift_multiplier_us"]) is { } us && us > 0)
        {
            m["us"] = Round(us);
        }
        return m;
    }

    /// <summary>Everything but the source links is equal.</summary>
    private static bool SameNumbers(JsonObject a, JsonObject b)
    {
        static JsonObject Strip(JsonObject o)
        {
            var c = (JsonObject)o.DeepClone();
            c.Remove("source");
            c.Remove("retiresSource");
            return c;
        }
        return JsonNode.DeepEquals(Strip(a), Strip(b));
    }

    private static void Copy(JsonObject e, string from, JsonObject m, string to)
    {
        if (Str(e, from) is { Length: > 0 } s)
        {
            m[to] = s;
        }
    }

    private static string? Str(JsonObject e, string key) => e[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static long? Int(JsonObject e, string key) => Num(e[key]) is { } d && d >= 1 && d == Math.Floor(d) ? (long)d : null;

    private static decimal? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? (decimal)d : null;

    /// <summary>USD per token → USD per 1M tokens.</summary>
    private static decimal? Usd(JsonObject e, string key) => Num(e[key]) is { } d && d >= 0 ? Round(d * 1_000_000m) : null;

    /// <summary>Rounded to 6 places without trailing zeros (2.500000 → 2.5), so the file reads as the provider's price list.</summary>
    private static decimal Round(decimal d) => Math.Round(d, 6) / 1.0000000000000000000000000000m;

    [GeneratedRegex("^[a-z0-9][a-z0-9._:@/-]*$")]
    internal static partial Regex KeyPattern();

    [GeneratedRegex(@"^(.+)-(\d{8}|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex Dated();

    [GeneratedRegex(@"^(input_cost_per_token|output_cost_per_token|cache_read_input_token_cost|cache_creation_input_token_cost|cache_creation_input_token_cost_above_1hr)_above_(\d+)k_tokens$")]
    private static partial Regex TierKey();
}
