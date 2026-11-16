// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class PluginContentsTests : IDisposable
{
    private static readonly AppDefinition _app = new() { Id = "app", Name = "App" };
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiparts-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text = "x")
    {
        var f = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    [Fact]
    public void Commands_and_agents_of_a_plugin_are_rows_of_that_plugin()
    {
        Write("p/.claude-plugin/plugin.json", """{ "name": "tools", "commands": ["./extra/ship.md", "./commands"], "agents": "./team" }""");
        Write("p/commands/review.md", "---\ndescription: Review the diff\n---\nbody");
        Write("p/commands/git/push.md", "push");
        Write("p/extra/ship.md", "---\ndescription: Ship it\n---\n");
        Write("p/extra/not-named.md", "x");
        Write("p/agents/planner.md", "---\nname: Planner\ndescription: Plans work\n---\n");
        Write("p/team/tester.md", "---\nname: tester\n---\n");
        var rows = new List<Item>();
        PluginManifests.Add(rows, new PluginContext(_app, "Project", "/work", null), Path.Combine(_tmp, "p"), id: "tools@market");

        var commands = rows.Where(r => r.Kind == "Command").OrderBy(r => r.Name).ToList();
        Assert.Equal(["git:push", "review", "ship"], commands.Select(r => r.Name));   // commands/ is named twice: one row each
        Assert.Equal("Review the diff", commands[1].Description);
        Assert.Equal(["Planner", "tester"], rows.Where(r => r.Kind == "Agent").Select(r => r.Name).Order());
        Assert.All(rows.Where(r => r.Kind != "Plugin"), r => Assert.Equal(("tools@market", "app", "Project", "/work", "App · tools"), (r.Plugin, r.App, r.Level, r.Project, r.Source)));
    }

    [Fact]
    public void Gemini_toml_commands_are_read_and_paths_outside_the_plugin_are_not()
    {
        Write("ext/gemini-extension.json", """{ "name": "conductor", "commands": "../elsewhere" }""");
        Write("ext/commands/plan.toml", "description = \"Make a plan\"\nprompt = \"x\"");
        Write("elsewhere/secret.md", "x");
        var rows = new List<Item>();
        PluginManifests.Add(rows, new PluginContext(_app, "User", null, null), Path.Combine(_tmp, "ext"));

        var plan = Assert.Single(rows, r => r.Kind == "Command");
        Assert.Equal(("plan", "Make a plan", "conductor"), (plan.Name, plan.Description, plan.Plugin));
    }
}
