// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class AppCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("apps-").FullName;
    private string File => Path.Combine(_dir, "apps.json");

    public void Dispose() => Directory.Delete(_dir, true);

    private static AppDefinition App(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Category = "agent"
    };

    [Fact]
    public void Built_in_apps_come_from_default_apps_json()
    {
        var builtIn = AppCatalog.BuiltIn();
        Assert.Contains(builtIn, d => d.Id == "claude-code");
        Assert.All(builtIn, d => Assert.Equal(AppOrigin.BuiltIn, d.Origin));
        Assert.Equal(builtIn.Count, builtIn.Select(d => d.Id).Distinct().Count());
        Assert.DoesNotContain(builtIn, d => d.Id.StartsWith(AppCatalog.CustomPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void A_custom_app_gets_the_prefix_and_is_listed_after_the_built_in_ones()
    {
        Assert.Null(AppCatalog.SaveUser(App("my-orch", "My Orchestrator"), File));
        var defs = AppCatalog.Load(File);
        var mine = defs[^1];
        Assert.Equal("custom.my-orch", mine.Id);
        Assert.Equal(AppOrigin.Custom, mine.Origin);
        var json = JsonNode.Parse(System.IO.File.ReadAllText(File))!;
        Assert.Equal(1, (int)json["version"]!);
        Assert.Equal("custom.my-orch", (string)json["apps"]![0]!["id"]!);
    }

    [Theory]
    [InlineData("claude-code", "Something")]
    [InlineData("custom.claude-code", "Something")]
    [InlineData("custom.other", " claude CODE ")]
    public void A_built_in_id_or_name_is_refused(string id, string name)
    {
        Assert.Contains("already supported", AppCatalog.SaveUser(App(id, name), File));
        Assert.False(System.IO.File.Exists(File));
    }

    [Fact]
    public void A_custom_app_for_a_tool_that_became_built_in_is_shadowed_until_removed()
    {
        System.IO.File.WriteAllText(File, """{ "version": 1, "apps": [ { "id": "custom.claude-code", "name": "Claude Code" } ] }""");
        var c = AppCatalog.Read(File);
        Assert.DoesNotContain(c.Defs, d => d.Id == "custom.claude-code");
        Assert.Single(c.Shadowed);

        AppCatalog.RemoveUser("custom.claude-code", File);
        Assert.Empty(AppCatalog.Read(File).Shadowed);
    }

    [Fact]
    public void Apps_saved_with_the_old_field_names_keep_their_skills_commands_and_agents()
    {
        System.IO.File.WriteAllText(File, """
            {"version": 1, "apps": [{"id": "custom.old", "name": "Old", "detect": ["~/.old"],
              "roots": [{"path": "~/.old/skills", "level": "User"}],
              "projectRoots": [{"path": ".old/skills"}, {"path": ".old/local", "level": "Local"}],
              "parts": [{"path": ".old/commands", "level": "Project"}, {"path": "~/.old/agents", "kind": "agent"}]}]}
            """);
        var d = Assert.Single(AppCatalog.Read(File).Defs, d => d.Id == "custom.old");
        Assert.Equal(["~/.old/skills:User", ".old/skills:Project", ".old/local:Local"], d.Skills.Select(s => s.Path + ":" + s.Level));
        Assert.Equal(".old/commands", Assert.Single(d.Commands).Path);
        Assert.Equal("~/.old/agents", Assert.Single(d.Agents).Path);

        AppCatalog.SaveUser(App("other", "Other"), File);   // written again: with the new names only
        var saved = System.IO.File.ReadAllText(File);
        Assert.Contains("\"skills\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"roots\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parts\"", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Built_in_apps_use_only_the_current_field_names()
    {
        using var s = typeof(AppCatalog).Assembly.GetManifestResourceStream("default/apps.json")!;
        var apps = JsonNode.Parse(s, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["apps"]!.AsArray();
        Assert.All(apps.OfType<JsonObject>(), a =>
        {
            Assert.False(a.ContainsKey("roots") || a.ContainsKey("projectRoots") || a.ContainsKey("parts"), $"{a["id"]}: use skills, commands and agents");
            Assert.All(new[] { "commands", "agents" }.SelectMany(k => a[k]?.AsArray() ?? []), p => Assert.Null(p!["kind"]));
        });
    }

    [Fact]
    public void Saving_the_same_app_again_replaces_it()
    {
        AppCatalog.SaveUser(App("x", "X one"), File);
        AppCatalog.SaveUser(App("custom.x", "X two"), File);
        Assert.Equal("X two", Assert.Single(AppCatalog.Read(File).Defs, d => d.Origin == AppOrigin.Custom).Name);
    }
}
