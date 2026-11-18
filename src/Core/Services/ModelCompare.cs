// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace AiStackprint.Services;

/// <summary>One axis of the Models comparison: the model field it reads and whether a bigger value is better (sizes) or a smaller one (prices).</summary>
public sealed record CompareAxis(string Label, string Field, bool HigherIsBetter);

/// <summary>One model on the radar: a percent (0 to 100) and the raw value for each axis; a raw value of null means the data has none (shown as 0 %).</summary>
public sealed record ModelScore(string Key, IReadOnlyList<double> Percent, IReadOnlyList<double?> Raw);

/// <summary>
/// Compares 2 to 4 models on six axes. Each axis is a percent on a scale from the worst to the best value among <b>all</b> models (not only the chosen ones):
/// the biggest size or the lowest price is 100 %, the smallest size or the highest price is 0 %. The scale is logarithmic, because sizes and prices
/// span orders of magnitude (32K to 2M tokens, $0.10 to $75), so a linear scale would put most models in a corner.
/// A free price is 100 %: on the price axes 100 % is kept for free, so the cheapest paid price is <see cref="PaidBest"/> % and the dearest 0 %.
/// </summary>
public static class ModelCompare
{
    public const int MinModels = 2, MaxModels = 4;

    /// <summary>The percent of the cheapest paid price; 100 % is only for free.</summary>
    public const double PaidBest = 95;

    public static readonly CompareAxis[] Axes =
    [
        new("Context", "contextWindow", true),
        new("Max output", "maxOutput", true),
        new("Cheap input", "price.input", false),
        new("Cheap output", "price.output", false),
        new("Cheap cache read", "price.cacheRead", false),
        new("Cheap cache write", "price.cacheWrite", false),
    ];

    /// <param name="models">The chosen models.</param>
    /// <param name="all">Every model: the scale of each axis runs from the worst to the best of these.</param>
    public static List<ModelScore> Score(IReadOnlyList<ModelRow> models, IReadOnlyList<ModelRow> all)
    {
        var range = Axes.Select(a =>
        {
            var v = all.Select(m => Parse(m, a.Field)).Where(d => d is > 0).Select(d => Math.Log(d!.Value)).ToList();
            return v.Count == 0 ? (0.0, 0.0, false) : (v.Min(), v.Max(), true);
        }).ToArray();
        return [.. models.Select(m =>
        {
            var raw = Axes.Select(a => Parse(m, a.Field)).ToArray();
            var pct = Axes.Select((a, i) => Percent(raw[i], a.HigherIsBetter, range[i])).ToArray();
            return new ModelScore(m.Key, pct, raw);
        })];
    }

    private static double Percent(double? v, bool higherIsBetter, (double Lo, double Hi, bool Any) r)
    {
        if (v == null)
        {
            return 0;
        }

        if (v <= 0)
        {
            return higherIsBetter ? 0 : 100;   // no size is the worst; a free price is the best
        }

        if (!r.Any || r.Hi - r.Lo < 1e-9)
        {
            return higherIsBetter ? 100 : PaidBest;   // every model has the same value
        }

        var share = (Math.Log(v.Value) - r.Lo) / (r.Hi - r.Lo);
        return higherIsBetter ? Math.Clamp(share, 0, 1) * 100 : Math.Clamp(1 - share, 0, 1) * PaidBest;
    }

    private static double? Parse(ModelRow m, string field) =>
        m.Values.TryGetValue(field, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
