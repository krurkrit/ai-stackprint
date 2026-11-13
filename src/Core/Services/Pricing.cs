using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>USD per 1M tokens.</summary>
public class ModelPrice
{
    public decimal Input { get; set; }
    public decimal Output { get; set; }
    public decimal CacheRead { get; set; }
    public decimal CacheWrite { get; set; }
}

public class PriceFile
{
    public string? Note { get; set; }
    public Dictionary<string, ModelPrice> Models { get; set; } = new();
}

/// <summary>
/// Cost estimates from prices.json (next to settings.json; created with a few defaults, then yours to edit).
/// A model gets the price of the longest key its name contains. Models without a price show tokens only.
/// </summary>
public static class Pricing
{
    public static string FilePath => Path.Combine(AppCatalog.Folder, "prices.json");

    static PriceFile? _file;
    static DateTime _stamp, _checked;
    static readonly object Gate = new();

    public static PriceFile Defaults() => new()
    {
        Note = "USD per 1M tokens. Estimates from public list prices: check them and edit to match your plan. A model gets the price of the longest key its name contains. Cache writes use the 5-minute price.",
        Models =
        {
            ["claude-opus-5-5"] = new() { Input = 4, Output = 20, CacheRead = 0.2m, CacheWrite = 5 },
            ["claude-sonnet-5-5"] = new() { Input = 2, Output = 10, CacheRead = 0.2m, CacheWrite = 2.5m }
        }
    };

    /// <summary>Creates prices.json with the defaults when it does not exist.</summary>
    public static void Ensure()
    {
        try
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(AppCatalog.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Defaults(), AppCatalog.Opt));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static PriceFile Load()
    {
        lock (Gate)
        {
            if (_file != null && DateTime.UtcNow - _checked < TimeSpan.FromSeconds(2)) return _file;
            _checked = DateTime.UtcNow;
            try
            {
                Ensure();
                var stamp = File.GetLastWriteTimeUtc(FilePath);
                if (_file == null || stamp != _stamp)
                {
                    _file = JsonSerializer.Deserialize<PriceFile>(File.ReadAllText(FilePath), AppCatalog.Opt) ?? Defaults();
                    _stamp = stamp;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _file ??= Defaults(); }
            return _file;
        }
    }

    public static ModelPrice? For(string model, PriceFile? table = null) =>
        (table ?? Load()).Models.Where(kv => kv.Key.Length > 0 && model.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)
                && kv.Value.Input + kv.Value.Output + kv.Value.CacheRead + kv.Value.CacheWrite > 0)   // an all-zero entry is a placeholder to fill in
            .OrderByDescending(kv => kv.Key.Length).Select(kv => kv.Value).FirstOrDefault();

    /// <summary>USD for a bucket: the app's own figure when it recorded one, else the estimate; null when neither.</summary>
    public static decimal? Cost(UsageBucket b, PriceFile? table = null) => b.Cost ?? Cost(b.Model, b.Input, b.Output, b.CacheRead, b.CacheWrite, table);

    public static decimal? Cost(string model, long input, long output, long cacheRead, long cacheWrite, PriceFile? table = null) =>
        For(model, table) is { } p ? (input * p.Input + output * p.Output + cacheRead * p.CacheRead + cacheWrite * p.CacheWrite) / 1_000_000m : null;

    /// <summary>Adds an all-zero placeholder for each model that has no entry yet, so you only have to fill in the numbers.</summary>
    public static void AddMissing(IEnumerable<string> models)
    {
        lock (Gate)
        {
            try
            {
                Ensure();
                var file = JsonSerializer.Deserialize<PriceFile>(File.ReadAllText(FilePath), AppCatalog.Opt) ?? Defaults();
                bool changed = false;
                foreach (var m in models.Distinct())
                    if (!file.Models.Keys.Any(k => k.Length > 0 && m.Contains(k, StringComparison.OrdinalIgnoreCase))) { file.Models[m] = new ModelPrice(); changed = true; }
                if (changed) { File.WriteAllText(FilePath, JsonSerializer.Serialize(file, AppCatalog.Opt)); _file = null; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }
}
