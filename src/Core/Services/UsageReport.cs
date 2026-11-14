// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One app's usage in the period.</summary>
public record AppUsage(AppDefinition App, UsageResult Result)
{
    /// <summary>Input, output and cache writes; cache reads apart (as the Usage card counts them).</summary>
    public long Tokens => Result.Buckets.Sum(b => b.Input + b.Output + b.CacheWrite);

    /// <summary>The app's own cost figures where its log has them, else an estimate from prices; null when no bucket has a price.</summary>
    public decimal? Cost => CostOf(Result.Buckets);

    public static decimal? CostOf(IEnumerable<UsageBucket> buckets)
    {
        decimal? total = null;
        foreach (var b in buckets)
        {
            if (Pricing.Cost(b) is { } c)
            {
                total = (total ?? 0) + c;
            }
        }

        return total;
    }
}

/// <summary>Usage across every app with readable logs (<see cref="UsageStats.Supports"/>), for one period. Read-only.</summary>
public static class UsageReport
{
    /// <param name="since">Only usage from this day on; DateTime.MinValue = all.</param>
    public static List<AppUsage> Compute(IEnumerable<AppDefinition> defs, IReadOnlyList<string> projects, DateTime since, CancellationToken ct = default)
    {
        var res = new List<AppUsage>();
        foreach (var d in defs.Where(UsageStats.Supports))
        {
            ct.ThrowIfCancellationRequested();
            var r = UsageStats.Compute(LogReader.Find(d, projects, usage: true), since, ct);
            if (r.Buckets.Count > 0)
            {
                res.Add(new(d, r));
            }
        }

        return [.. res.OrderByDescending(a => a.Tokens)];
    }
}
