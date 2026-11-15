// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>USD per 1M tokens. A missing value is unknown, not free (default/models.schema.json).</summary>
public class ModelPrice
{
    public decimal? Input { get; set; }
    public decimal? Output { get; set; }
    public decimal? CacheRead { get; set; }
    /// <summary>Short cache (Anthropic: 5 minutes).</summary>
    public decimal? CacheWrite { get; set; }
    /// <summary>Anthropic 1-hour cache.</summary>
    public decimal? CacheWrite1h { get; set; }

    /// <summary>Input and output are known: enough for a cost.</summary>
    public bool Known => Input != null && Output != null;
}

/// <summary>A long-context price: a request whose input is over <see cref="Tokens"/> is priced at this tier; a field left out uses the base price.</summary>
public sealed class PriceTier : ModelPrice
{
    public long Tokens { get; set; }
}

/// <summary>One model of default/models.json, with your changes from ~/.ai-stackprint/models.json on top.</summary>
public sealed class ModelInfo
{
    public string Provider { get; set; } = "custom";
    public long? ContextWindow { get; set; }
    public long? MaxOutput { get; set; }
    public ModelPrice? Price { get; set; }
    public List<PriceTier>? Above { get; set; }
    /// <summary>fast (fast mode), us (US-only processing).</summary>
    public Dictionary<string, decimal>? Multipliers { get; set; }
    /// <summary>USD per web search.</summary>
    public decimal? WebSearch { get; set; }
    public string? Retires { get; set; }
    public string? Successor { get; set; }
    public string? RetiresSource { get; set; }
    public string? Source { get; set; }
}

/// <summary>The model data in use: built-in or downloaded (the newer), with your changes. Not changed once built: changed files make a new table.</summary>
public sealed class ModelTable
{
    public Dictionary<string, ModelInfo> Models { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Date of the data (YYYY-MM-DD), from its <c>updated</c>.</summary>
    public string? Updated { get; init; }
    /// <summary>The downloaded copy is used (newer than the built-in one).</summary>
    public bool Downloaded { get; init; }
}
