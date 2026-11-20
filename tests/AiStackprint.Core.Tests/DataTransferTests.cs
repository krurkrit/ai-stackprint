// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Export and import between two data folders ("here" and "there").</summary>
public sealed class DataTransferTests : IDisposable
{
    private static readonly DateOnly _today = new(2026, 11, 16);
    private static readonly DateTime _now = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Local);
    private static readonly JsonObject _models = JsonNode.Parse("""
        { "claude-opus-5-5": { "provider": "anthropic", "contextWindow": 1000000, "price": { "input": 4, "output": 20 } } }
        """)!.AsObject();

    private readonly string _root = Directory.CreateTempSubdirectory("transfer-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private DataFiles Files(string name)
    {
        var d = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        return new(Path.Combine(d, "settings.json"), Path.Combine(d, "models.json"), Path.Combine(d, "apps.json"), Path.Combine(d, "projects.json"), Path.Combine(d, "backups"), _root);
    }

    private static JsonObject Export(DataFiles f, DataParts parts = DataParts.All, bool all = false) =>
        JsonNode.Parse(DataExport.Build(parts, all, f, _today, _models).ToJsonString())!.AsObject();

    private static ImportPlan Plan(JsonObject export, DataFiles f) => DataImport.Plan(export.ToJsonString(), f, _models);

    private (DataFiles There, string Project) Filled()
    {
        var there = Files("there");
        var s = SettingsStore.Load(there.Settings);
        s.Theme = "Dark";
        s.SetSize(16);
        s.CheckUpdates = true;
        s.LimitsEnabled = ["claude-code"];
        SettingsStore.Save(s, there.Settings);
        ModelCatalog.Save(_models, there.Models, "claude-opus-5-5", new Dictionary<string, string?> { ["price.input"] = "5" });
        ModelCatalog.Save(_models, there.Models, "my-model", new Dictionary<string, string?> { ["price.input"] = "1", ["price.output"] = "2" });
        AppCatalog.SaveUser(new AppDefinition { Id = "my-tool", Name = "My Tool", Category = "cli" }, there.Apps);
        var project = Directory.CreateDirectory(Path.Combine(_root, "work", "demo")).FullName;
        ProjectStore.Save([new(project, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)), new(Path.Combine(_root, "gone"), DateTime.UtcNow)], there.Projects, there.Home);
        return (there, project);
    }

    [Fact]
    public void Only_my_changes_holds_each_part_with_home_paths_as_tilde()
    {
        var (there, _) = Filled();
        var e = Export(there);
        Assert.Equal(1, (int)e["version"]!);
        Assert.Equal("changes", (string)e["values"]!);
        Assert.Equal("Dark", (string)e["settings"]!["theme"]!);
        Assert.Equal("""{"body":{"size":{"value":16}}}""", e["settings"]!["fonts"]!.ToJsonString());   // only the changed size; the defaults are not a change
        Assert.Equal("""{"claude-opus-5-5":{"price":{"input":5}},"my-model":{"provider":"custom","price":{"input":1,"output":2}}}""", e["models"]!.ToJsonString());
        Assert.Equal("custom.my-tool", (string)e["apps"]![0]!["id"]!);
        Assert.Equal("~/work/demo", (string)e["projects"]![0]!["path"]!);
        Assert.Equal(["version", "exported", "values", "settings", "apps", "projects", "models"], e.Select(k => k.Key));   // the parts in the order of the Export dialog
        Assert.Null(Export(there, DataParts.Settings)["models"]);
    }

    [Fact]
    public void All_values_holds_defaults_and_the_model_data()
    {
        var (there, _) = Filled();
        var e = Export(there, DataParts.Settings | DataParts.Models, all: true);
        Assert.Equal("all", (string)e["values"]!);
        Assert.Equal("Inter", (string)e["settings"]!["fonts"]!["body"]!["family"]!["value"]!);
        Assert.Equal(1000000, (long)e["models"]!["claude-opus-5-5"]!["contextWindow"]!);
        Assert.Equal(5, (decimal)e["models"]!["claude-opus-5-5"]!["price"]!["input"]!);
    }

    [Fact]
    public void A_round_trip_gives_the_same_data_and_consents_stay_off()
    {
        var (there, project) = Filled();
        var here = Files("here");
        var plan = Plan(Export(there), here);
        Assert.Contains(plan.Changes, c => c.Part == DataParts.Settings && c.What == "theme" && c.After == "Dark");
        Assert.DoesNotContain(plan.Changes, c => DataImport.Consents.Contains(c.What));
        Assert.Contains(plan.Notes, n => n.Contains("checkUpdates", StringComparison.Ordinal));
        Assert.Equal([Path.Combine(_root, "gone")], plan.MissingProjects);

        var backup = DataImport.Apply(plan, here, new Dictionary<string, string>(), _now, _models);
        Assert.NotNull(backup);
        var s = SettingsStore.Load(here.Settings);
        Assert.Equal(("Dark", 16d, false), (s.Theme, s.Font().Size, s.CheckUpdates));
        Assert.Empty(s.LimitsEnabled);
        Assert.Equal(Export(there)["models"]!.ToJsonString(), Export(here)["models"]!.ToJsonString());
        Assert.Equal("custom.my-tool", Assert.Single(AppCatalog.Read(here.Apps).Defs, d => d.Origin == AppOrigin.Custom).Id);
        Assert.Equal([project], ProjectStore.Load(here.Projects, here.Home).Select(p => p.Path));
        Assert.Empty(Plan(Export(there), here).Changes);   // nothing left to change
    }

    [Fact]
    public void Importing_merges_over_mine_backs_up_first_and_keeps_the_rest()
    {
        var (there, _) = Filled();
        var here = Files("here");
        var s = SettingsStore.Load(here.Settings);
        s.SetSize(13);
        s.SetWeight(600, FontRoles.Menu);
        SettingsStore.Save(s, here.Settings);
        var before = File.ReadAllText(here.Settings);
        ModelCatalog.Save(_models, here.Models, "claude-opus-5-5", new Dictionary<string, string?> { ["price.output"] = "30" });

        var backup = DataImport.Apply(Plan(Export(there), here), here, new Dictionary<string, string>(), _now, _models)!;
        Assert.Equal(before, File.ReadAllText(Path.Combine(backup, "settings.json")));
        s = SettingsStore.Load(here.Settings);
        Assert.Equal((16d, 600), (s.Font().Size, s.Font(FontRoles.Menu).Weight));   // the import's value wins; mine that it lacks stay
        Assert.Equal("""{"price":{"input":5,"output":30}}""", ModelCatalog.ReadOverrides(here.Models)!["claude-opus-5-5"]!.ToJsonString());
    }

    [Fact]
    public void All_values_keep_only_what_differs_here_and_a_missing_project_can_be_located()
    {
        var (there, _) = Filled();
        var here = Files("here");
        var plan = Plan(Export(there, all: true), here);
        Assert.True(plan.AllValues);
        var moved = Directory.CreateDirectory(Path.Combine(_root, "found")).FullName;
        DataImport.Apply(plan, here, new Dictionary<string, string> { [Path.Combine(_root, "gone")] = moved }, _now, _models);
        Assert.Equal(["theme", "fonts"], JsonNode.Parse(File.ReadAllText(here.Settings))!.AsObject().Select(k => k.Key).Where(k => k != "version").Order().Reverse());
        Assert.Equal("""{"price":{"input":5}}""", ModelCatalog.ReadOverrides(here.Models)!["claude-opus-5-5"]!.ToJsonString());
        Assert.Contains(moved, ProjectStore.Load(here.Projects, here.Home).Select(p => p.Path));
    }

    [Theory]
    [InlineData("""{ "version": 2, "exported": "2027-01-01" }""", "newer AI Stackprint")]
    [InlineData("""{ "theme": "Dark" }""", "not an AI Stackprint export")]
    [InlineData("{ nope", "not valid JSON")]
    public void A_file_that_is_newer_or_not_an_export_is_refused(string json, string why)
    {
        var e = Assert.Throws<ImportRefusedException>(() => DataImport.Plan(json, Files("here"), _models));
        Assert.Contains(why, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_that_is_built_in_now_is_noted_and_not_imported()
    {
        var here = Files("here");
        var json = """{ "version": 1, "exported": "2026-11-16", "values": "changes", "apps": [ { "id": "custom.claude-code", "name": "Claude Code", "category": "agent" } ] }""";
        var plan = DataImport.Plan(json, here, _models);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Notes, n => n.Contains("built in", StringComparison.Ordinal));
        Assert.Null(DataImport.Apply(plan, here, new Dictionary<string, string>(), _now, _models));
    }
}
