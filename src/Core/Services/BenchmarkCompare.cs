// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>
/// The Benchmarks radar: eight axes from Artificial Analysis, each a percent from the worst (0 %) to the best (100 %) of all their models, not only the chosen ones.
/// Scores are 0 to 100 already and scale linearly; speed, time to first token and cost per task span orders of magnitude and scale logarithmically.
/// </summary>
public static class BenchmarkCompare
{
    public static readonly string[] Fields = ["intelligence", "engineering", "finance", "legal", "strategy", "speed", "firstToken", "costPerTask"];

    public static readonly CompareAxis[] Axes =
    [
        new("Intelligence", "intelligence", true),
        new("Engineering", "engineering", true),
        new("Finance", "finance", true),
        new("Legal", "legal", true),
        new("Strategy and ops", "strategy", true),
        new("Speed", "speed", true),
        new("Fast first token", "firstToken", false),
        new("Cheap per task", "costPerTask", false),
    ];

    private static bool Log(string field) => field is "speed" or "firstToken" or "costPerTask";

    /// <param name="Scores">One per chosen model that has benchmark rows, in the order chosen (percent 0 to 100 and the raw value of each axis).</param>
    /// <param name="Variants">The name of the row each score comes from, e.g. "Claude Sonnet 5.5 (High)": a model is run at several efforts, the best is used.</param>
    /// <param name="Missing">Chosen models without a row.</param>
    public sealed record Result(List<ModelScore> Scores, List<string> Variants, List<string> Missing);

    public static Result Score(IReadOnlyList<string> keys, IReadOnlyCollection<string> catalogKeys, IReadOnlyList<Benchmark> all)
    {
        var range = Axes.Select(a =>
        {
            var v = all.Select(b => b.Get(a.Field)).Where(d => d is > 0).Select(d => Log(a.Field) ? Math.Log(d!.Value) : d!.Value).ToList();
            return v.Count == 0 ? (0.0, 0.0) : (v.Min(), v.Max());
        }).ToArray();
        var scores = new List<ModelScore>(); var variants = new List<string>(); var missing = new List<string>();
        foreach (var key in keys)
        {
            if (BenchmarkStore.Best(key, catalogKeys, all) is not { } b)
            {
                missing.Add(key);
                continue;
            }

            var raw = Axes.Select(a => b.Get(a.Field)).ToArray();
            var pct = Axes.Select((a, i) => Percent(raw[i], a, range[i])).ToArray();
            scores.Add(new(key, pct, raw));
            variants.Add(b.Name);
        }

        return new(scores, variants, missing);
    }

    private static double Percent(double? v, CompareAxis a, (double Lo, double Hi) r)
    {
        if (v is not > 0)
        {
            return 0;
        }

        if (r.Hi - r.Lo < 1e-9)
        {
            return 100;
        }

        var share = ((Log(a.Field) ? Math.Log(v.Value) : v.Value) - r.Lo) / (r.Hi - r.Lo);
        return Math.Clamp(a.HigherIsBetter ? share : 1 - share, 0, 1) * 100;
    }
}
