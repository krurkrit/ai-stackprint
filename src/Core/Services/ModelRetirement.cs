// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Health check: config files that pin a model which is retired or retires soon (the model data's <c>retires</c>), with its successor and source.
/// Only the values of keys named like "model" are read (JSON and TOML); nothing else in the file is kept.
/// </summary>
public static partial class ModelRetirement
{
    /// <summary>A retirement this many days away or less is a warning; later ones are information.</summary>
    internal const int SoonDays = 90;

    public static List<HealthIssue> ForHealth(IEnumerable<Item> items, DateOnly today, ModelTable? table = null)
    {
        var res = new List<HealthIssue>();
        foreach (var i in items.Where(i => i.Kind == "Config" && Path.GetExtension(i.Path).ToLowerInvariant() is ".json" or ".jsonc" or ".toml" && File.Exists(i.Path)))
        {
            List<string> pinned;
            try
            {
                var text = ContextBudget.ReadText(i.Path);
                pinned = [.. Pinned(text, i.Path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { continue; }

            foreach (var (key, info) in pinned.Select(m => Find(m, table)).OfType<(string, ModelInfo)>().DistinctBy(m => m.Item1))
            {
                if (DateOnly.TryParseExact(info.Retires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day <= today.AddDays(365))
                {
                    res.Add(new HealthIssue("Models", i.Name, i.App, Finding(key, info, day, today), i));
                }
            }
        }
        return res;
    }

    internal static Finding Finding(string key, ModelInfo info, DateOnly day, DateOnly today)
    {
        var when = day <= today ? $"was retired on {day:yyyy-MM-dd}" : $"retires {day:yyyy-MM-dd}";
        var use = info.Successor is { Length: > 0 } s ? $"; use {s}" : "";
        var source = info.RetiresSource is { Length: > 0 } src ? $" Source: {src}" : "";
        return new($"Pins {key}, which {when}{use}.{source}", day <= today.AddDays(SoonDays));
    }

    /// <summary>The string values of keys whose name contains "model", in a JSON (comments allowed) or TOML file.</summary>
    internal static IEnumerable<string> Pinned(string text, bool toml)
    {
        if (toml)
        {
            return TomlModel().Matches(text).Select(m => m.Groups[1].Value);
        }

        using var doc = JsonDocument.Parse(text, ConfigJson.Jsonc);
        var found = new List<string>();
        Walk(doc.RootElement, found);
        return found;
    }

    private static void Walk(JsonElement e, List<string> found)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.Name.Contains("model", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(p.Value.GetString()!);
                }
                else
                {
                    Walk(p.Value, found);
                }
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray())
            {
                Walk(x, found);
            }
        }
    }

    /// <summary>The model a pinned value names: the provider prefix (<c>anthropic/</c>) dropped, then the longest model key it contains.</summary>
    private static (string, ModelInfo)? Find(string value, ModelTable? table)
    {
        var name = value[(value.LastIndexOf('/') + 1)..].Trim();
        if (name.Length is < 2 or > 100 || name.Contains(' '))
        {
            return null;
        }

        return ModelCatalog.Info(name, table) is { } info && ModelCatalog.InfoKey(name, table) is { } key ? (key, info) : null;
    }

    [GeneratedRegex("""^[ \t]*[\w.-]*model[\w.-]*[ \t]*=[ \t]*"([^"\r\n]+)"[ \t]*(?:#.*)?$""", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex TomlModel();
}
