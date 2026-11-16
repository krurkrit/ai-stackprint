// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One part of what a config file holds: "3 MCP servers", "model claude-opus-5-5".</summary>
/// <param name="Page">The page that lists these ("Mcp", "Hooks"); null when no page does.</param>
public sealed record HoldsPart(string Text, string? Page = null);

/// <summary>
/// What a config file holds, so a settings file explains itself instead of being a bare path: its servers and hooks (from the scan's lists),
/// the models it names, its permission rules, and an ignore file's pattern count. Values that look secret are hidden.
/// </summary>
public static class ConfigSummary
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxModels = 2;

    public static List<HoldsPart> Of(Item config, IReadOnlyList<McpServer> servers, IReadOnlyList<Hook> hooks)
    {
        var res = new List<HoldsPart>();
        bool Here(string file) => string.Equals(file, config.Path, StringComparison.OrdinalIgnoreCase) || ContextBudget.Inside(file, config.Path);
        foreach (var g in servers.Where(s => s.Plugin == null && Here(s.File)).GroupBy(s => s.Kind).OrderBy(g => g.Key == "MCP" ? 0 : 1))   // MCP first
        {
            res.Add(new(Plural(g.Count(), g.Key + " server"), "Mcp"));
        }

        var hookCount = hooks.Count(h => h.Plugin == null && Here(h.File));
        if (hookCount > 0)
        {
            res.Add(new(Plural(hookCount, "hook"), "Hooks"));
        }

        if (!File.Exists(config.Path) || new FileInfo(config.Path).Length > MaxBytes)
        {
            return res;
        }

        if (config.Description == "Ignore")
        {
            try { res.Add(new(Plural(File.ReadLines(config.Path).Count(l => l.Trim() is { Length: > 0 } t && !t.StartsWith('#')), "pattern"))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        else if (config.Description is "Settings" or "MCP servers")
        {
            res.AddRange(Settings(config.Path));
        }

        return res;
    }

    /// <summary>The models a settings file names and its permission rules (allow, ask, deny).</summary>
    private static IEnumerable<HoldsPart> Settings(string path)
    {
        var toml = path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
        if (!toml && !Path.GetExtension(path).StartsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        string text;
        List<string> models;
        try
        {
            text = ContextBudget.ReadText(path);
            models = [.. ModelRetirement.Pinned(text, toml).Where(m => m.Length > 0).Select(ConfigJson.Mask).Distinct()];
        }
        catch (JsonException) { yield break; }

        if (models.Count > 0)
        {
            yield return new((models.Count == 1 ? "model " : "models ") + string.Join(", ", models.Take(MaxModels)) + (models.Count > MaxModels ? $" +{models.Count - MaxModels}" : ""));
        }

        if (!toml && Rules(text) is > 0 and var rules)
        {
            yield return new(Plural(rules, "permission rule"));
        }
    }

    private static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n:N0} {what}s";

    private static int Rules(string text)
    {
        using var doc = JsonDocument.Parse(text, ConfigJson.Jsonc);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("permissions", out var perm) || perm.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        return new[] { "allow", "ask", "deny" }.Sum(k => ConfigJson.Strings(perm, k).Count);
    }
}
