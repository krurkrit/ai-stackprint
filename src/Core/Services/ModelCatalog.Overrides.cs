// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>Your changes to the model data (models.json in the data folder), and the move from the older prices.json.</summary>
public static partial class ModelCatalog
{
    private static readonly VersionedJson _format = new("models", 1);
    private static bool _migrated;

    /// <summary>Your changes: only the fields you set, per model, and models you added (<c>provider: custom</c>).</summary>
    public static string OverridesFile => Path.Combine(DataFolder.Root, "models.json");

    /// <summary>The prices file of versions before 0.4, moved into <see cref="OverridesFile"/> on the first load.</summary>
    public static string OldPricesFile => Path.Combine(DataFolder.Root, "prices.json");

    /// <summary>Shown once when models.json comes from a newer AI Stackprint: changes to it are not saved.</summary>
    public static string? Problem => _format.IsNewer(OverridesFile)
        ? "models.json comes from a newer AI Stackprint: model changes made here are not saved."
        : null;

    /// <summary>The models object of your file, or null when there is none or it cannot be read.</summary>
    internal static JsonObject? ReadOverrides(string file)
    {
        try { return _format.Read(file, Backups(file))?["models"] as JsonObject; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void MigrateOnce()
    {
        if (!_migrated)
        {
            _migrated = true;
            MigratePrices(OldPricesFile, OverridesFile, _builtIn.Value["models"] as JsonObject ?? []);
        }
    }

    /// <summary>
    /// prices.json → your models.json: entries with a price you set (not an all-zero placeholder, not one of the two defaults it was created with)
    /// become changes, only the fields that differ from the model data; unknown models are added as <c>custom</c>. The old file goes to backups/.
    /// </summary>
    internal static void MigratePrices(string prices, string overrides, JsonObject models)
    {
        if (!File.Exists(prices))
        {
            return;
        }

        try
        {
            var old = JsonNode.Parse(File.ReadAllText(prices), documentOptions: _read)?["models"] as JsonObject ?? [];
            var mine = ReadOverrides(overrides) ?? [];
            var changed = false;
            foreach (var (key, node) in old)
            {
                if (node is not JsonObject p || OldFields(p) is not { Count: > 0 } set || IsOldDefault(key, set) || mine.ContainsKey(key))
                {
                    continue;
                }

                var basePrice = models[key]?["price"] as JsonObject;
                var diff = new JsonObject();
                foreach (var (f, v) in set)
                {
                    if (Num(basePrice?[f]) != (decimal)v)
                    {
                        diff[f] = v.DeepClone();
                    }
                }

                if (diff.Count == 0)
                {
                    continue;   // the same as the model data now
                }

                mine[key] = models.ContainsKey(key) ? new JsonObject { ["price"] = diff } : new JsonObject { ["provider"] = "custom", ["price"] = diff };
                changed = true;
            }

            if (changed)
            {
                _format.Write(overrides, new JsonObject { ["models"] = mine });
            }

            var backups = Backups(prices);
            Directory.CreateDirectory(backups);
            var dest = Path.Combine(backups, "prices.json");
            File.Move(prices, File.Exists(dest) ? Path.Combine(backups, $"prices.{DateTime.Now:yyyyMMdd-HHmmss}.json") : dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }   // tried again on the next start
    }

    /// <summary>The non-zero prices of an old entry, by their new names (the old file had the same names).</summary>
    private static Dictionary<string, JsonNode> OldFields(JsonObject p)
    {
        var set = new Dictionary<string, JsonNode>();
        foreach (var f in new[] { "input", "output", "cacheRead", "cacheWrite" })
        {
            var v = p.FirstOrDefault(kv => string.Equals(kv.Key, f, StringComparison.OrdinalIgnoreCase)).Value;
            if (Num(v) is { } d && d > 0)
            {
                set[f] = JsonValue.Create(d);
            }
        }
        return set;
    }

    /// <summary>prices.json was created with these two entries; left as they were, they are not your changes.</summary>
    private static bool IsOldDefault(string key, Dictionary<string, JsonNode> set)
    {
        decimal[]? d = key switch { "claude-opus-5-5" => [4, 20, 0.2m, 5], "claude-sonnet-5-5" => [2, 10, 0.2m, 2.5m], _ => null };
        string[] names = ["input", "output", "cacheRead", "cacheWrite"];
        return d != null && set.Count == 4 && names.Select((n, i) => (decimal)set[n] == d[i]).All(x => x);
    }

    /// <summary>A JSON number read from a file or built in code, as decimal (2.50 and 2.5 are the same).</summary>
    private static decimal? Num(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == JsonValueKind.Number
            && decimal.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Backups(string file) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "backups");
}
