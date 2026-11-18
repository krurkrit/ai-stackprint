// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>One model's benchmark row from Artificial Analysis; a missing value is null. Speed is output tokens per second, first token and cost per task are lower-is-better.</summary>
public sealed record Benchmark(string Slug, string Name, string Creator, double? Intelligence, double? Engineering, double? Finance, double? Legal, double? Strategy, double? Speed, double? FirstToken, double? CostPerTask)
{
    public double? Get(string field) => field switch
    {
        "intelligence" => Intelligence,
        "engineering" => Engineering,
        "finance" => Finance,
        "legal" => Legal,
        "strategy" => Strategy,
        "speed" => Speed,
        "firstToken" => FirstToken,
        "costPerTask" => CostPerTask,
        _ => null
    };
}

/// <summary>The benchmark rows saved in cache/benchmarks.json, and which of them belong to a model of the catalog.</summary>
public static class BenchmarkStore
{
    public const int Version = 2;
    public static string CacheFile => Path.Combine(DataFolder.Cache, "benchmarks.json");

    /// <summary>The rows of the saved copy; empty when there is none or it is not readable.</summary>
    public static List<Benchmark> Load(string? file = null)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file ?? CacheFile)) is JsonObject o && o["version"]?.GetValue<int>() == Version && o["models"] is JsonArray a)
            {
                return [.. a.OfType<JsonObject>().Select(Read).OfType<Benchmark>()];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { }
        return [];
    }

    public static void Save(IReadOnlyList<Benchmark> rows, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var models = new JsonArray();
        foreach (var b in rows)
        {
            var o = new JsonObject { ["slug"] = b.Slug, ["name"] = b.Name, ["creator"] = b.Creator };
            foreach (var f in BenchmarkCompare.Fields)
            {
                if (b.Get(f) is { } v)
                {
                    o[f] = v;
                }
            }

            models.Add(o);
        }

        var tmp = file + ".tmp";
        File.WriteAllText(tmp, new JsonObject { ["version"] = Version, ["models"] = models }.ToJsonString());
        File.Move(tmp, file, overwrite: true);
    }

    private static Benchmark? Read(JsonObject o)
    {
        double? N(string k) => o[k] is JsonValue v && v.TryGetValue(out double d) ? d : null;
        return o["slug"]?.GetValue<string>() is { Length: > 0 } slug
            ? new(slug, o["name"]?.GetValue<string>() ?? slug, o["creator"]?.GetValue<string>() ?? "", N("intelligence"), N("engineering"), N("finance"), N("legal"), N("strategy"), N("speed"), N("firstToken"), N("costPerTask"))
            : null;
    }

    private static readonly HashSet<string> _suffix = ["minimal", "low", "medium", "high", "xhigh", "max", "reasoning", "non", "thinking", "default", "fallback", "adaptive", "instruct"];

    private static string Norm(string s) => s.ToLowerInvariant().Replace('.', '-').Replace('_', '-');

    /// <summary>Slug words after a model key that only say how it was run (effort, reasoning, a date or token budget), not another model.</summary>
    private static bool OnlyVariant(string rest) =>
        rest.Split('-', StringSplitOptions.RemoveEmptyEntries).All(w => _suffix.Contains(w) || (w.All(char.IsDigit) && w.Length is 2 or 4 or 8) || (w.EndsWith('k') && w[..^1].All(char.IsDigit)));

    /// <summary>
    /// The rows that are the model <paramref name="key"/> run in different ways: the slug is the key, or the key plus effort, reasoning or date words.
    /// A slug goes to the longest catalog key it starts with, so "gpt-5-mini-high" is never counted for "gpt-5".
    /// </summary>
    public static List<Benchmark> Variants(string key, IReadOnlyCollection<string> catalogKeys, IReadOnlyList<Benchmark> all)
    {
        var k = Norm(key);
        var longer = catalogKeys.Select(Norm).Where(o => o.Length > k.Length && o.StartsWith(k + "-", StringComparison.Ordinal)).ToList();
        return [.. all.Where(b =>
        {
            var s = Norm(b.Slug);
            return (s == k || (s.StartsWith(k + "-", StringComparison.Ordinal) && OnlyVariant(s[(k.Length + 1)..]))) && !longer.Any(o => s == o || s.StartsWith(o + "-", StringComparison.Ordinal));
        })];
    }

    /// <summary>The variant with the highest intelligence index (then the most values), or null when the model has no row.</summary>
    public static Benchmark? Best(string key, IReadOnlyCollection<string> catalogKeys, IReadOnlyList<Benchmark> all) =>
        Variants(key, catalogKeys, all).OrderByDescending(b => b.Intelligence ?? -1).ThenByDescending(b => BenchmarkCompare.Fields.Count(f => b.Get(f) != null)).FirstOrDefault();
}
