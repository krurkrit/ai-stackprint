// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ModelRetirementTests : IDisposable
{
    private static readonly DateOnly _today = new(2026, 11, 15);
    private readonly string _dir = Directory.CreateTempSubdirectory("retire-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static ModelTable Table()
    {
        var t = new ModelTable();
        t.Models["claude-sonnet-4-5"] = new ModelInfo { Retires = "2026-11-30", Successor = "claude-sonnet-5-5", RetiresSource = "https://example.com/deprecations" };
        t.Models["gpt-4.1"] = new ModelInfo { Retires = "2027-09-01" };
        t.Models["o3"] = new ModelInfo { Retires = "2026-01-01" };
        t.Models["claude-opus-5-5"] = new ModelInfo();
        return t;
    }

    private Item Config(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return new Item("Config", name, "Settings", "App", "User", path) { App = "app" };
    }

    [Fact]
    public void A_pinned_model_that_retires_soon_is_a_warning_with_its_successor_and_source()
    {
        var item = Config("settings.json", """{ "model": "claude-sonnet-4-5-20250929", "env": { "TOKEN": "x" } } // comment""");
        var issue = Assert.Single(ModelRetirement.ForHealth([item], _today, Table()));
        Assert.Equal("Models", issue.Area);
        Assert.True(issue.Finding.Warning);
        Assert.Equal("Pins claude-sonnet-4-5, which retires 2026-11-30; use claude-sonnet-5-5. Source: https://example.com/deprecations", issue.Finding.Text);
        Assert.Same(item, issue.Item);
    }

    [Fact]
    public void Retired_is_a_warning_later_is_information_and_far_or_unknown_dates_are_left_out()
    {
        var item = Config("opencode.json", """{ "agent": { "a": { "model": "openai/o3" }, "b": { "small_model": "gpt-4.1" } }, "provider": { "model": "claude-opus-5-5" } }""");
        var issues = ModelRetirement.ForHealth([item], _today, Table());
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Finding.Warning && i.Finding.Text.StartsWith("Pins o3, which was retired on 2026-01-01.", StringComparison.Ordinal));
        Assert.Contains(issues, i => !i.Finding.Warning && i.Finding.Text.StartsWith("Pins gpt-4.1", StringComparison.Ordinal));
        Assert.DoesNotContain(ModelRetirement.ForHealth([item], new DateOnly(2025, 1, 1), Table()), i => i.Finding.Text.Contains("gpt-4.1", StringComparison.Ordinal));   // over a year away
    }

    [Fact]
    public void Toml_values_of_model_keys_are_read_and_other_keys_are_not()
    {
        var item = Config("config.toml", "model = \"o3\"  # pinned\nreview_model = \"gpt-4.1\"\nnote = \"claude-sonnet-4-5\"\n[profiles.x]\nmodel=\"claude-opus-5-5\"\n");
        var found = ModelRetirement.ForHealth([item], _today, Table()).Select(i => i.Finding.Text.Split(',')[0]).Order().ToList();
        Assert.Equal(["Pins gpt-4.1", "Pins o3"], found);
    }

    [Fact]
    public void Not_valid_json_and_other_kinds_are_skipped()
    {
        var bad = Config("broken.json", "{ \"model\": ");
        var skill = new Item("Skill", "s", "", "App", "User", Config("SKILL.json", """{ "model": "o3" }""").Path);
        Assert.Empty(ModelRetirement.ForHealth([bad, skill], _today, Table()));
    }
}
