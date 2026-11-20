// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ModelCatalogEditTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("modeledit-").FullName;
    private string File_ => Path.Combine(_dir, "models.json");

    private static readonly JsonObject _data = JsonNode.Parse("""
        {
          "claude-opus-5-5": { "provider": "anthropic", "contextWindow": 1000000, "maxOutput": 128000, "price": { "input": 4, "output": 20, "cacheRead": 0.4 }, "retires": "2027-06-01" },
          "gpt-6": { "provider": "openai", "contextWindow": 400000 }
        }
        """)!.AsObject();

    public void Dispose() => Directory.Delete(_dir, true);

    private JsonObject Saved() => JsonNode.Parse(File.ReadAllText(File_))!["models"]!.AsObject();

    private ModelRow Row(string key) => ModelCatalog.Rows(_data, File_).Single(r => r.Key == key);

    [Fact]
    public void Rows_list_the_data_with_its_values_and_nothing_changed()
    {
        var rows = ModelCatalog.Rows(_data, File_);
        Assert.Equal(["claude-opus-5-5", "gpt-6"], rows.Select(r => r.Key));
        var opus = rows[0];
        Assert.Equal("anthropic", opus.Provider);
        Assert.Equal("1000000", opus.Values["contextWindow"]);
        Assert.Equal("0.4", opus.Values["price.cacheRead"]);
        Assert.Equal("2027-06-01", opus.Values["retires"]);
        Assert.False(opus.Values.ContainsKey("price.cacheWrite"));
        Assert.Empty(opus.Changed);
        Assert.False(opus.Added);
    }

    [Fact]
    public void Only_values_that_differ_from_the_data_are_written()
    {
        Assert.Null(ModelCatalog.Save(_data, File_, "claude-opus-5-5", new Dictionary<string, string?>
        {
            ["price.input"] = "5",
            ["price.output"] = "20",
            ["contextWindow"] = "1,000,000"
        }));
        Assert.Equal("""{"claude-opus-5-5":{"price":{"input":5}}}""", Saved().ToJsonString());
        var row = Row("claude-opus-5-5");
        Assert.Equal("5", row.Values["price.input"]);
        Assert.Equal("4", row.Data["price.input"]);
        Assert.Equal(["price.input"], row.Changed);
    }

    [Fact]
    public void An_empty_value_or_the_data_value_removes_the_change()
    {
        ModelCatalog.Save(_data, File_, "claude-opus-5-5", new Dictionary<string, string?>
        {
            ["price.input"] = "5",
            ["maxOutput"] = "64000"
        });
        ModelCatalog.Save(_data, File_, "claude-opus-5-5", new Dictionary<string, string?>
        {
            ["price.input"] = "4",
            ["maxOutput"] = ""
        });
        Assert.Equal("{}", Saved().ToJsonString());
    }

    [Fact]
    public void Reset_removes_one_field_or_the_whole_model()
    {
        ModelCatalog.Save(_data, File_, "claude-opus-5-5", new Dictionary<string, string?>
        {
            ["price.input"] = "5",
            ["retires"] = "2027-01-01"
        });
        ModelCatalog.Reset(File_, "claude-opus-5-5", "price.input");
        Assert.Equal("""{"claude-opus-5-5":{"retires":"2027-01-01"}}""", Saved().ToJsonString());
        Assert.Equal(["retires"], Row("claude-opus-5-5").Changed);
        ModelCatalog.Reset(File_, "claude-opus-5-5", null);
        Assert.Equal("{}", Saved().ToJsonString());
    }

    [Fact]
    public void A_model_not_in_the_data_is_added_as_custom_and_removed_by_reset()
    {
        Assert.Null(ModelCatalog.Save(_data, File_, " My-Model ", new Dictionary<string, string?>
        {
            ["price.input"] = "1.5",
            ["price.output"] = "3"
        }));
        var row = Row("my-model");
        Assert.True(row.Added);
        Assert.Equal("custom", row.Provider);
        Assert.Equal("1.5", row.Values["price.input"]);
        Assert.Empty(row.Changed);
        ModelCatalog.Reset(File_, "my-model", null);
        Assert.DoesNotContain(ModelCatalog.Rows(_data, File_), r => r.Key == "my-model");
    }

    [Theory]
    [InlineData("Bad Name!", "price.input", "1", "model name")]
    [InlineData("x", "price.input", "-1", "Input")]
    [InlineData("x", "contextWindow", "1.5", "Context")]
    [InlineData("x", "retires", "next year", "YYYY-MM-DD")]
    public void Bad_values_are_refused_and_nothing_is_written(string key, string field, string value, string shown)
    {
        var why = ModelCatalog.Save(_data, File_, key, new Dictionary<string, string?> { [field] = value });
        Assert.Contains(shown, why, StringComparison.Ordinal);
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void Other_models_in_the_file_are_kept()
    {
        File.WriteAllText(File_, """{ "version": 1, "models": { "codex-auto-review": { "provider": "custom", "price": {} } } }""");
        ModelCatalog.Save(_data, File_, "gpt-6", new Dictionary<string, string?> { ["maxOutput"] = "128000" });
        Assert.Equal("""{"codex-auto-review":{"provider":"custom","price":{}},"gpt-6":{"maxOutput":128000}}""", Saved().ToJsonString());
        Assert.True(Row("codex-auto-review").Added);
    }
}
