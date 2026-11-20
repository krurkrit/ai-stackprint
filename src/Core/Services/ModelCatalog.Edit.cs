// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>A model on the Models page: its values in use, which of them are yours, and whether you added it.</summary>
/// <param name="Values">The values in use by field path (<see cref="ModelCatalog.Fields"/>); missing = unknown.</param>
/// <param name="Data">The model data's own values (empty for a model you added).</param>
/// <param name="Changed">Field paths where your value is used instead of the data's.</param>
public sealed record ModelRow(string Key, string Provider, IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string> Data, IReadOnlySet<string> Changed, bool Added);

/// <summary>Editing your changes from the Models page: one model at a time; only values that differ from the model data are written.</summary>
public static partial class ModelCatalog
{
    /// <summary>The fields the Models page shows and edits, as paths in a model.</summary>
    public static readonly string[] Fields = ["contextWindow", "maxOutput", "price.input", "price.output", "price.cacheRead", "price.cacheWrite", "price.cacheWrite1h", "retires"];

    /// <summary>Every model in the data and every one you added, with your changes, sorted by name.</summary>
    public static List<ModelRow> Rows() => Rows(SourceModels(), OverridesFile);

    /// <summary>Saves your values for one model (field path → text; empty = the data's value). Returns why it was refused, or null.</summary>
    public static string? Save(string key, IReadOnlyDictionary<string, string?> values) => Save(SourceModels(), OverridesFile, key, values);

    /// <summary>Removes your value for one field, or (no field) all your changes to the model; a model you added is removed.</summary>
    public static string? Reset(string key, string? field) => Reset(OverridesFile, key, field);

    public static string? Reset(string key) => Reset(key, null);

    /// <summary>A short name for a field path, as the page shows it.</summary>
    public static string Label(string field) => field switch
    {
        "contextWindow" => "Context",
        "maxOutput" => "Max output",
        "price.input" => "Input",
        "price.output" => "Output",
        "price.cacheRead" => "Cache read",
        "price.cacheWrite" => "Cache write",
        "price.cacheWrite1h" => "Cache write 1h",
        "retires" => "Retires",
        _ => field,
    };

    internal static List<ModelRow> Rows(JsonObject data, string file)
    {
        var mine = ReadOverrides(file) ?? [];
        var rows = new List<ModelRow>();
        foreach (var key in data.Select(m => m.Key).Union(mine.Select(m => m.Key)).Order(StringComparer.Ordinal))
        {
            var theirs = data[key] as JsonObject;
            var yours = mine[key] as JsonObject;
            if (theirs == null && yours == null)
            {
                continue;
            }

            var used = theirs != null && yours != null ? JsonLayers.Merge(theirs, yours) : (yours ?? theirs)!;
            var values = Values(used);
            var dataValues = theirs != null ? Values(theirs) : [];
            var changed = theirs == null ? [] : Fields.Where(f => Get(yours, f) is JsonValue && values.GetValueOrDefault(f) != dataValues.GetValueOrDefault(f)).ToHashSet();
            rows.Add(new(key, Str(used["provider"]) ?? "custom", values, dataValues, changed, theirs == null));
        }
        return rows;
    }

    internal static string? Save(JsonObject data, string file, string key, IReadOnlyDictionary<string, string?> values)
    {
        key = key.Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(key))
        {
            return "A model name uses lowercase letters, digits and . _ : @ / - (as the apps log it).";
        }

        foreach (var (f, v) in values)
        {
            if (!string.IsNullOrWhiteSpace(v) && Parse(f, v.Trim()) == null)
            {
                return $"{Label(f)}: {Expected(f)}.";
            }
        }

        if (_format.IsNewer(file))
        {
            return Problem;
        }

        var theirs = data[key] as JsonObject;
        var mine = ReadOverrides(file) ?? [];
        var yours = mine[key] as JsonObject ?? (theirs == null ? new JsonObject { ["provider"] = "custom" } : []);
        foreach (var (f, v) in values)
        {
            var parsed = string.IsNullOrWhiteSpace(v) ? null : Parse(f, v.Trim());
            Set(yours, f, parsed == null || (theirs != null && JsonNode.DeepEquals(Get(theirs, f), parsed)) ? null : parsed);
        }

        if (yours.Count == 0)
        {
            mine.Remove(key);
        }
        else if (yours.Parent == null)
        {
            mine[key] = yours;
        }

        return WriteOverrides(mine, file);
    }

    internal static string? Reset(string file, string key, string? field)
    {
        var mine = ReadOverrides(file) ?? [];
        if (mine[key] is not JsonObject yours)
        {
            return null;
        }

        if (field != null)
        {
            Set(yours, field, null);
        }

        if (field == null || yours.Count == 0)
        {
            mine.Remove(key);
        }

        return WriteOverrides(mine, file);
    }

    /// <summary>The model data in use before your changes: built-in, or the downloaded copy when it is newer.</summary>
    internal static JsonObject SourceModels()
    {
        var downloaded = ReadCache(CacheFile);
        var source = downloaded != null && string.CompareOrdinal(Updated(downloaded), Updated(_builtIn.Value)) > 0 ? downloaded : _builtIn.Value;
        return source["models"] as JsonObject ?? [];
    }

    internal static string? WriteOverrides(JsonObject mine, string file)
    {
        try
        {
            _format.Write(file, new JsonObject { ["models"] = mine.DeepClone() });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "models.json could not be saved: " + e.Message;
        }

        lock (_gate)
        {
            _table = null;   // read again on the next use
        }
        return null;
    }

    private static string Expected(string field) =>
        field == "retires" ? "a date as YYYY-MM-DD"
        : field.StartsWith("price.", StringComparison.Ordinal) ? "a number of at least 0 (USD per 1M tokens)"
        : "a whole number of at least 1";

    private static Dictionary<string, string> Values(JsonObject model)
    {
        var d = new Dictionary<string, string>();
        foreach (var f in Fields)
        {
            if (Get(model, f) is JsonValue v)
            {
                d[f] = v.TryGetValue(out string? s) ? s : v.ToJsonString();
            }
        }
        return d;
    }

    private static JsonNode? Parse(string field, string text)
    {
        if (field == "retires")
        {
            return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? JsonValue.Create(text) : null;
        }

        if (field.StartsWith("price.", StringComparison.Ordinal))
        {
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 ? JsonValue.Create(d / 1.0000000000000000000000000000m) : null;
        }

        return long.TryParse(text.Replace(",", "", StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 ? JsonValue.Create(n) : null;
    }

    private static JsonNode? Get(JsonObject? model, string path)
    {
        JsonNode? n = model;
        foreach (var part in path.Split('.'))
        {
            n = n is JsonObject o ? o[part] : null;
        }
        return n;
    }

    /// <summary>Sets, or (null) removes, a value; the price object left empty is removed too.</summary>
    private static void Set(JsonObject model, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var o = model;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (o[parts[i]] is not JsonObject inner)
            {
                if (value == null)
                {
                    return;
                }

                o[parts[i]] = inner = [];
            }
            o = inner;
        }

        if (value == null)
        {
            o.Remove(parts[^1]);
            if (parts.Length > 1 && o.Count == 0)
            {
                model.Remove(parts[0]);
            }
        }
        else
        {
            o[parts[^1]] = value.DeepClone();
        }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9._:@/-]*$")]
    private static partial Regex KeyPattern();
}
