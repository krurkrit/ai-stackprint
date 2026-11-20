// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>
/// Model data: the built-in default/models.json (generated weekly from LiteLLM), or the downloaded copy in cache/ when it is newer, with your
/// changes from models.json in the data folder on top, field by field (models you add there are new keys). A logged model name uses the longest
/// key it contains, so dated, Bedrock and Vertex names find their model; models without a price show tokens only.
/// </summary>
public static partial class ModelCatalog
{
    private static readonly JsonDocumentOptions _read = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly Lazy<JsonObject> _builtIn = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("default/models.json")!;
        return JsonNode.Parse(s, documentOptions: _read)!.AsObject();
    });

    private static ModelTable? _table;
    private static (DateTime, DateTime) _stamps;
    private static DateTime _checked;
    private static readonly Lock _gate = new();

    /// <summary>The downloaded copy (phase 7: Settings › Updates); kept per user.</summary>
    public static string CacheFile => Path.Combine(DataFolder.Cache, "models.json");

    /// <summary>The model data in use; files are checked for changes at most every 2 seconds.</summary>
    public static ModelTable Load()
    {
        lock (_gate)
        {
            if (_table != null && DateTime.UtcNow - _checked < TimeSpan.FromSeconds(2))
            {
                return _table;
            }

            _checked = DateTime.UtcNow;
            MigrateOnce();
            var stamps = (Stamp(CacheFile), Stamp(OverridesFile));
            if (_table == null || stamps != _stamps)
            {
                _table = Build(_builtIn.Value, ReadCache(CacheFile), ReadOverrides(OverridesFile));
                _stamps = stamps;
            }
            return _table;
        }
    }

    /// <summary>Built-in or downloaded data, whichever has the later <c>updated</c>, then <paramref name="overrides"/> (the models object of your file) field by field.</summary>
    internal static ModelTable Build(JsonObject builtIn, JsonObject? downloaded, JsonObject? overrides)
    {
        var useDownloaded = downloaded != null && string.CompareOrdinal(Updated(downloaded), Updated(builtIn)) > 0;
        var source = useDownloaded ? downloaded! : builtIn;
        var merged = (JsonObject)(source["models"] as JsonObject ?? []).DeepClone();
        foreach (var (key, node) in overrides ?? [])
        {
            if (node is JsonObject mine)
            {
                merged[key] = merged[key] is JsonObject theirs ? JsonLayers.Merge(theirs, mine) : mine.DeepClone();
            }
        }

        var table = new ModelTable
        {
            Updated = Updated(source),
            Downloaded = useDownloaded
        };
        foreach (var (key, node) in merged)
        {
            try
            {
                if (node?.Deserialize<ModelInfo>(AppCatalog._opt) is { } m)
                {
                    table.Models[key] = m;
                }
            }
            catch (JsonException) { }   // one model with a value of the wrong type does not lose the others
        }
        return table;
    }

    /// <summary>
    /// The model with a price for a logged name: the longest key it contains. Each name is looked up once per table (costs are asked for
    /// every usage event, and a log holds a handful of names).
    /// </summary>
    public static ModelInfo? For(string model, ModelTable? table) => Lookup(model, table).Priced;

    public static ModelInfo? For(string model) => For(model, null);

    /// <summary>The model for a logged name, priced or not (context window, retirement).</summary>
    public static ModelInfo? Info(string model, ModelTable? table) => Lookup(model, table).Any;

    public static ModelInfo? Info(string model) => Info(model, null);

    /// <summary>The key that <see cref="For(string, ModelTable)"/> used ("priced as …"), or null.</summary>
    public static string? PricedAs(string model, ModelTable? table) => Lookup(model, table).PricedKey;

    public static string? PricedAs(string model) => PricedAs(model, null);

    /// <summary>The key that <see cref="Info(string, ModelTable)"/> used, or null.</summary>
    public static string? InfoKey(string model, ModelTable? table) => Lookup(model, table).AnyKey;

    public static string? InfoKey(string model) => InfoKey(model, null);

    /// <summary>USD for a bucket: the app's own figure when it recorded one, else the estimate; null when neither.</summary>
    public static decimal? Cost(UsageBucket b, ModelTable? table) =>
        b.Cost ?? Estimate(b.Model, new(b.Input, b.Output, b.CacheRead, b.CacheWrite, b.CacheWrite1h, b.Tier, b.Fast, b.UsOnly, b.WebSearches), table);

    public static decimal? Cost(UsageBucket b) => Cost(b, null);

    /// <summary>USD for one reply: the app's own figure, else the estimate with its tier.</summary>
    public static decimal? Cost(UsageEvent e, ModelTable? table) =>
        e.Cost ?? Estimate(e.Model, new(e.Input, e.Output, e.CacheRead, e.CacheWrite, e.CacheWrite1h, TierOf(e.Model, e.RequestInput, table), e.Fast, e.UsOnly, e.WebSearches), table);

    public static decimal? Cost(UsageEvent e) => Cost(e, null);

    /// <summary>An estimate at the base price (short cache, no tier).</summary>
    public static decimal? Cost(string model, long input, long output, long cacheRead, long cacheWrite, ModelTable? table) =>
        Estimate(model, new(input, output, cacheRead, cacheWrite, 0, 0, false, false, 0), table);

    public static decimal? Cost(string model, long input, long output, long cacheRead, long cacheWrite) => Cost(model, input, output, cacheRead, cacheWrite, null);

    /// <summary>The tier a request of <paramref name="requestInput"/> tokens is priced at: the largest <c>tokens</c> it is over, or 0 (base price).</summary>
    public static long TierOf(string model, long requestInput, ModelTable? table) =>
        For(model, table)?.Above?.Where(t => requestInput > t.Tokens).Select(t => t.Tokens).DefaultIfEmpty(0).Max() ?? 0;

    public static long TierOf(string model, long requestInput) => TierOf(model, requestInput, null);

    /// <summary>The web-search part of a cost (USD), or null when the model has no search price.</summary>
    public static decimal? SearchCost(string model, long searches, ModelTable? table) => For(model, table)?.WebSearch is { } w ? searches * w : null;

    public static decimal? SearchCost(string model, long searches) => SearchCost(model, searches, null);

    private readonly record struct Use(long Input, long Output, long CacheRead, long CacheWrite, long CacheWrite1h, long Tier, bool Fast, bool UsOnly, long WebSearches);

    /// <summary>
    /// Tokens at the tier's prices (a field the tier leaves out uses the base price), 1-hour cache writes at <c>cacheWrite1h</c>, times the fast and
    /// US-only multipliers, plus web searches. A cache price that is unknown counts at the input price (a 1-hour one at the short-cache price).
    /// </summary>
    private static decimal? Estimate(string model, Use u, ModelTable? table)
    {
        if (For(model, table) is not { Price: { Input: { } i0, Output: { } o0 } p } m)
        {
            return null;
        }

        var tier = u.Tier > 0 ? m.Above?.Find(t => t.Tokens == u.Tier) : null;
        decimal i = tier?.Input ?? i0, o = tier?.Output ?? o0;
        var read = tier?.CacheRead ?? p.CacheRead ?? i;
        var write = tier?.CacheWrite ?? p.CacheWrite ?? i;
        var write1h = tier?.CacheWrite1h ?? p.CacheWrite1h ?? write;
        var cost = (u.Input * i + u.Output * o + u.CacheRead * read + (u.CacheWrite - u.CacheWrite1h) * write + u.CacheWrite1h * write1h) / 1_000_000m;
        if (u.Fast && m.Multipliers?.GetValueOrDefault("fast") is > 0 and var fast)
        {
            cost *= fast;
        }

        if (u.UsOnly && m.Multipliers?.GetValueOrDefault("us") is > 0 and var us)
        {
            cost *= us;
        }

        return cost + u.WebSearches * (m.WebSearch ?? 0);
    }

    private sealed record Found(ModelInfo? Priced, string? PricedKey, ModelInfo? Any, string? AnyKey);

    private static readonly ConditionalWeakTable<ModelTable, ConcurrentDictionary<string, Found>> _found = [];

    private static Found Lookup(string model, ModelTable? table)
    {
        var t = table ?? Load();
        return _found.GetValue(t, static _ => new(StringComparer.OrdinalIgnoreCase)).GetOrAdd(model, Find, t);
    }

    private static Found Find(string model, ModelTable table)
    {
        (ModelInfo? Info, string? Key) priced = default, any = default;
        foreach (var (key, info) in table.Models)
        {
            if (key.Length == 0 || !model.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (key.Length > (any.Key?.Length ?? 0))
            {
                any = (info, key);
            }

            if (info.Price?.Known == true && key.Length > (priced.Key?.Length ?? 0))
            {
                priced = (info, key);
            }
        }
        return new(priced.Info, priced.Key, any.Info, any.Key);
    }

    private static string Updated(JsonObject file) => file["updated"] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    private static DateTime Stamp(string file)
    {
        try { return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    /// <summary>The downloaded copy when it is a version 1 file with models; anything else is not used.</summary>
    internal static JsonObject? ReadCache(string file)
    {
        try
        {
            return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file), documentOptions: _read) is JsonObject o
                && o["version"] is JsonValue v && v.TryGetValue(out int n) && n == 1 && o["models"] is JsonObject ? o : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
