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
    private static readonly JsonDocumentOptions _read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
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

        var table = new ModelTable { Updated = Updated(source), Downloaded = useDownloaded };
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
    public static ModelInfo? For(string model, ModelTable? table = null) => Lookup(model, table).Priced;

    /// <summary>The model for a logged name, priced or not (context window, retirement).</summary>
    public static ModelInfo? Info(string model, ModelTable? table = null) => Lookup(model, table).Any;

    /// <summary>The key that <see cref="For"/> used ("priced as …"), or null.</summary>
    public static string? PricedAs(string model, ModelTable? table = null) => Lookup(model, table).PricedKey;

    /// <summary>USD for a bucket: the app's own figure when it recorded one, else the estimate; null when neither.</summary>
    public static decimal? Cost(UsageBucket b, ModelTable? table = null) => b.Cost ?? Cost(b.Model, b.Input, b.Output, b.CacheRead, b.CacheWrite, table);

    /// <summary>An estimate at the base price; a cache price that is unknown is counted at the input price.</summary>
    public static decimal? Cost(string model, long input, long output, long cacheRead, long cacheWrite, ModelTable? table = null) =>
        For(model, table)?.Price is { Input: { } i, Output: { } o } p
            ? (input * i + output * o + cacheRead * (p.CacheRead ?? i) + cacheWrite * (p.CacheWrite ?? i)) / 1_000_000m
            : null;

    private sealed record Found(ModelInfo? Priced, string? PricedKey, ModelInfo? Any);

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
        return new(priced.Info, priced.Key, any.Info);
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
