// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace AiStackprint.ModelData;

/// <summary>What must hold before a new default/models.json is proposed, and what a reviewer should look at.</summary>
public static class Checks
{
    public static readonly int MaxBytes = 500_000;
    internal const double MinShare = 0.8;
    private static readonly string[] _priceFields = ["input", "output", "cacheRead", "cacheWrite", "cacheWrite1h"];
    private static readonly HashSet<string> _modelFields = ["provider", "contextWindow", "maxOutput", "price", "above", "multipliers", "webSearch", "retires", "successor", "retiresSource", "source"];

    /// <summary>Reasons not to use the new file: it breaks the schema, a provider has no models, it lost over 20% of the models, or it is too large.</summary>
    public static List<string> Problems(JsonObject file, JsonObject? current, int bytes)
    {
        var p = new List<string>();
        if (file["models"] is not JsonObject models)
        {
            return ["The file has no models."];
        }

        foreach (var (name, node) in models)
        {
            Model(name, node, p);
        }

        foreach (var provider in Generator.Providers)
        {
            if (!models.Any(m => (string?)m.Value?["provider"] == provider))
            {
                p.Add($"No {provider} models: LiteLLM may have renamed the provider.");
            }
        }

        if (current?["models"] is JsonObject old && models.Count < old.Count * MinShare)
        {
            p.Add($"{models.Count} models, fewer than {MinShare:P0} of the current {old.Count}.");
        }

        if (bytes > MaxBytes)
        {
            p.Add($"{bytes:N0} bytes, over the {MaxBytes:N0} limit.");
        }
        return p;
    }

    /// <summary>Prices that went up or down more than 10 times: likely a unit mistake upstream.</summary>
    public static List<string> BigChanges(JsonObject file, JsonObject? current)
    {
        var res = new List<string>();
        if (current?["models"] is not JsonObject old || file["models"] is not JsonObject models)
        {
            return res;
        }

        foreach (var (name, node) in models)
        {
            foreach (var f in _priceFields)
            {
                if (Usd(node?["price"]?[f]) is { } now && Usd(old[name]?["price"]?[f]) is { } was && was > 0 && now > 0 && (now / was > 10 || was / now > 10))
                {
                    res.Add($"`{name}` {f}: {was.ToString(CultureInfo.InvariantCulture)} → {now.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }
        return res;
    }

    /// <summary>Pull request text: source, counts, models added and removed, big price changes.</summary>
    public static string Report(JsonObject file, JsonObject? current)
    {
        var models = (JsonObject)file["models"]!;
        var old = current?["models"] as JsonObject ?? [];
        var added = models.Select(m => m.Key).Where(k => !old.ContainsKey(k)).ToList();
        var removed = old.Select(m => m.Key).Where(k => !models.ContainsKey(k)).ToList();
        var changed = models.Count(m => old[m.Key] is { } o && !JsonNode.DeepEquals(o, m.Value));
        var commit = (string?)file["source"]?["commit"] ?? "";
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Model data from [LiteLLM]({Generator.SourceUrl}/blob/{commit}/model_prices_and_context_window.json) at `{commit[..Math.Min(7, commit.Length)]}` (MIT).");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {models.Count} models ({string.Join(", ", Generator.Providers.Select(p => $"{p} {models.Count(m => (string?)m.Value?["provider"] == p)}"))})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {added.Count} added, {removed.Count} removed, {changed} changed");
        List("Added", added, sb);
        List("Removed", removed, sb);
        var big = BigChanges(file, current);
        if (big.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Price changes over 10×: check these against the provider's page before merging.**");
            big.ForEach(b => sb.AppendLine("- " + b));
        }
        return sb.ToString();
    }

    private static void List(string title, List<string> names, StringBuilder sb)
    {
        if (names.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{title}: {string.Join(", ", names.Take(60).Select(n => $"`{n}`"))}{(names.Count > 60 ? $" and {names.Count - 60} more" : "")}");
        }
    }

    private static void Model(string name, JsonNode? node, List<string> p)
    {
        if (!Generator.KeyPattern().IsMatch(name))
        {
            p.Add($"`{name}`: not a valid key.");
        }

        if (node is not JsonObject m)
        {
            p.Add($"`{name}`: not an object.");
            return;
        }

        foreach (var (k, _) in m)
        {
            if (!_modelFields.Contains(k))
            {
                p.Add($"`{name}`: unknown field {k}.");
            }
        }

        if (!Generator.Providers.Contains((string?)m["provider"]))
        {
            p.Add($"`{name}`: unknown provider.");
        }

        foreach (var f in new[] { "contextWindow", "maxOutput" })
        {
            if (m[f] is { } n && (Usd(n) is not { } v || v < 1 || v != Math.Floor(v)))
            {
                p.Add($"`{name}`: {f} is not a whole number of at least 1.");
            }
        }

        if (m["price"] is { } price && (price is not JsonObject po || Usd(po["input"]) == null || Usd(po["output"]) == null || !Prices(po)))
        {
            p.Add($"`{name}`: price needs input and output, all at least 0.");
        }

        if (m["above"] is JsonArray above && above.Select(t => Usd(t?["tokens"]) ?? 0).ToList() is var tokens
            && (tokens.Any(t => t < 1) || !tokens.SequenceEqual(tokens.Order()) || above.Any(t => t is not JsonObject to || !Prices(to))))
        {
            p.Add($"`{name}`: tiers need tokens of at least 1, lowest first, and prices of at least 0.");
        }

        foreach (var f in new[] { "retires" })
        {
            if (m[f] is { } d && !DateOnly.TryParseExact((string?)d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                p.Add($"`{name}`: {f} is not a date (YYYY-MM-DD).");
            }
        }
    }

    private static bool Prices(JsonObject o) => _priceFields.All(f => o[f] is null || Usd(o[f]) is >= 0);

    /// <summary>Any JSON number (a value read from a file or one built in code, whatever .NET type holds it).</summary>
    private static decimal? Usd(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            && decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
