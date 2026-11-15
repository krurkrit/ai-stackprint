// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("models-").FullName;
    private string Overrides => Path.Combine(_dir, "models.json");
    private string Prices => Path.Combine(_dir, "prices.json");

    public void Dispose() => Directory.Delete(_dir, true);

    private static JsonObject Data(string updated, string models) =>
        JsonNode.Parse($$"""{ "version": 1, "updated": "{{updated}}", "models": {{models}} }""")!.AsObject();

    private static readonly JsonObject _builtIn = Data("2026-11-15", """
        {
          "claude-sonnet-4-5": { "provider": "anthropic", "contextWindow": 200000, "price": { "input": 3, "output": 15, "cacheRead": 0.3 }, "retires": "2026-11-30" },
          "claude-opus-5-5": { "provider": "anthropic", "price": { "input": 4, "output": 20 }, "multipliers": { "fast": 2 } },
          "grok-5": { "provider": "xai", "contextWindow": 2000000 }
        }
        """);

    [Fact]
    public void The_built_in_data_has_the_models_the_apps_log()
    {
        var t = ModelCatalog.Build(JsonNode.Parse(ResourceText())!.AsObject(), null, null);
        Assert.False(t.Downloaded);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", t.Updated);
        Assert.True(t.Models.Count > 100);
        Assert.NotNull(ModelCatalog.For("claude-sonnet-4-5-20250929", t));
        Assert.Equal("claude-sonnet-4-5", ModelCatalog.PricedAs("us.anthropic.claude-sonnet-4-5-20250929-v1:0", t));
    }

    [Fact]
    public void Your_changes_go_over_the_data_field_by_field_and_add_models()
    {
        var mine = JsonNode.Parse("""
            {
              "claude-sonnet-4-5": { "price": { "input": 2.5 } },
              "my-local-model": { "provider": "custom", "price": { "input": 0, "output": 0 } }
            }
            """)!.AsObject();
        var t = ModelCatalog.Build(_builtIn, null, mine);
        var sonnet = t.Models["claude-sonnet-4-5"];
        Assert.Equal((2.5m, 15m, 0.3m), (sonnet.Price!.Input!.Value, sonnet.Price.Output!.Value, sonnet.Price.CacheRead!.Value));
        Assert.Equal(200000, sonnet.ContextWindow);
        Assert.Equal("2026-11-30", sonnet.Retires);
        Assert.Equal(0m, ModelCatalog.Cost("my-local-model:7b", 1000, 1000, 0, 0, t));
    }

    [Fact]
    public void The_downloaded_copy_is_used_only_when_it_is_newer()
    {
        var newer = Data("2026-12-01", """{ "claude-opus-5-5": { "provider": "anthropic", "price": { "input": 3, "output": 15 } } }""");
        var older = Data("2026-10-01", """{ "claude-opus-5-5": { "provider": "anthropic", "price": { "input": 9, "output": 9 } } }""");
        var t = ModelCatalog.Build(_builtIn, newer, null);
        Assert.True(t.Downloaded);
        Assert.Equal(3, ModelCatalog.For("claude-opus-5-5", t)!.Price!.Input);
        Assert.Equal(4, ModelCatalog.For("claude-opus-5-5", ModelCatalog.Build(_builtIn, older, null))!.Price!.Input);
    }

    [Fact]
    public void A_downloaded_copy_of_another_version_or_without_models_is_not_used()
    {
        var file = Path.Combine(_dir, "cache.json");
        File.WriteAllText(file, """{ "version": 2, "updated": "2027-01-01", "models": {} }""");
        Assert.Null(ModelCatalog.ReadCache(file));
        File.WriteAllText(file, "{ not json");
        Assert.Null(ModelCatalog.ReadCache(file));
        File.WriteAllText(file, """{ "version": 1, "updated": "2027-01-01", "models": {} }""");
        Assert.NotNull(ModelCatalog.ReadCache(file));
    }

    [Fact]
    public void A_model_without_a_price_is_found_for_its_size_but_gives_no_cost()
    {
        var t = ModelCatalog.Build(_builtIn, null, null);
        Assert.Null(ModelCatalog.For("grok-5-mini", t));
        Assert.Equal(2000000, ModelCatalog.Info("grok-5-mini", t)!.ContextWindow);
        Assert.Equal(2m, ModelCatalog.For("claude-opus-5-5", t)!.Multipliers!["fast"]);
    }

    [Fact]
    public void Prices_json_becomes_your_changes_without_placeholders_or_its_old_defaults_and_moves_to_backups()
    {
        File.WriteAllText(Prices, """
            {
              "note": "USD per 1M tokens.",
              "models": {
                "claude-opus-5-5": { "input": 4, "output": 20, "cacheRead": 0.2, "cacheWrite": 5 },
                "claude-sonnet-4-5": { "input": 3, "output": 12, "cacheRead": 0.3, "cacheWrite": 0 },
                "gpt-local": { "input": 1, "output": 2, "cacheRead": 0, "cacheWrite": 0 },
                "placeholder": { "input": 0, "output": 0, "cacheRead": 0, "cacheWrite": 0 }
              }
            }
            """);

        ModelCatalog.MigratePrices(Prices, Overrides, (JsonObject)_builtIn["models"]!);

        var mine = ModelCatalog.ReadOverrides(Overrides)!;
        Assert.Equal("""{"claude-sonnet-4-5":{"price":{"output":12}},"gpt-local":{"provider":"custom","price":{"input":1,"output":2}}}""", mine.ToJsonString());
        Assert.False(File.Exists(Prices));
        Assert.True(File.Exists(Path.Combine(_dir, "backups", "prices.json")));
    }

    [Fact]
    public void Prices_json_with_nothing_of_yours_writes_no_changes()
    {
        File.WriteAllText(Prices, """{ "models": { "claude-opus-5-5": { "input": 4, "output": 20, "cacheRead": 0.2, "cacheWrite": 5 } } }""");
        ModelCatalog.MigratePrices(Prices, Overrides, (JsonObject)_builtIn["models"]!);
        Assert.False(File.Exists(Overrides));
        Assert.False(File.Exists(Prices));
    }

    [Fact]
    public void A_model_of_yours_with_an_empty_price_has_no_price()
    {
        File.WriteAllText(Overrides, """{ "version": 1, "models": { "my-model": { "provider": "custom", "price": {} } } }""");
        Assert.Null(ModelCatalog.For("my-model", ModelCatalog.Build(_builtIn, null, ModelCatalog.ReadOverrides(Overrides))));   // empty: still unknown
    }

    private static string ResourceText()
    {
        using var s = typeof(ModelCatalog).Assembly.GetManifestResourceStream("default/models.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
