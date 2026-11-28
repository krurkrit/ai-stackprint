// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The Add app dialog looks into a config folder and builds the app from what the user keeps.</summary>
public sealed class AppFolderProbeTests : IDisposable
{
    private readonly string _name = "probe-" + Guid.NewGuid().ToString("N");
    private string Dir => Path.Combine(TestHome.Root, _name);   // inside the (test) home folder, so it is written as ~

    public AppFolderProbeTests() => Directory.CreateDirectory(Dir);

    public void Dispose() => Directory.Delete(Dir, true);

    private void Make(string rel)
    {
        var path = Path.Combine(Dir, rel);
        if (rel.EndsWith('/'))
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
    }

    [Fact]
    public void Known_folders_and_files_are_found_in_a_fixed_order()
    {
        foreach (var rel in new[] { "logs/", "mcp.json", "AGENTS.md", "agents/", "Commands/", "skills/", "settings.json", "notes.txt", "cache/" })
        {
            Make(rel);
        }

        Assert.Equal(["skills:skills", "commands:Commands", "agents:agents", "instructions:AGENTS.md", "settings:settings.json", "mcp:mcp.json", "logs:logs"],
            AppFolderProbe.Look(Dir).Select(f => f.Kind + ":" + f.Rel));
    }

    [Fact]
    public void Skill_files_without_a_skills_folder_make_the_folder_itself_a_skills_folder()
    {
        Make("packs/review/SKILL.md");
        var f = Assert.Single(AppFolderProbe.Look(Dir));
        Assert.Equal(("skills", ""), (f.Kind, f.Rel));
        Assert.Equal(new AppEntry("skills", "~/" + _name, "User"), Assert.Single(AppFolderProbe.Entries(Dir, [f])));
    }

    [Fact]
    public void A_missing_or_empty_folder_finds_nothing()
    {
        Assert.Empty(AppFolderProbe.Look(Path.Combine(Dir, "nope")));
        Assert.Empty(AppFolderProbe.Look(""));
        Assert.Empty(AppFolderProbe.Look(Dir));
    }

    [Fact]
    public void What_was_found_becomes_rows_of_the_apps_json_lists_at_User_level()
    {
        Make("skills/");
        Make("AGENTS.md");
        Make("mcp.json");
        Make("logs/");
        Assert.Equal(
            [new("skills", $"~/{_name}/skills", "User"), new("configs", $"~/{_name}/AGENTS.md", "User", "instructions"), new("configs", $"~/{_name}/mcp.json", "User", "mcp"), new("logs", $"~/{_name}/logs", "User")],
            AppFolderProbe.Entries(Dir, AppFolderProbe.Look(Dir)));
    }

    [Fact]
    public void Each_row_goes_in_its_list_and_empty_rows_are_left_out()
    {
        var d = AppFolderProbe.Build(" My Agent! ", "agent", Dir + "/",
        [
            new("skills", $"{Dir}/skills", "User"), new("skills", ".myapp/skills", "Project"), new("configs", ".myapp/mcp.json", "Project", "mcp"),
            new("commands", ".myapp/commands", "Project"), new("agents", "~/x/agents", "User"), new("logs", "~/x/logs", "User"), new("agents", "  ", "User")
        ]);

        Assert.Equal(("custom.my-agent", "My Agent!"), (d.Id, d.Name));   // the name as typed, trimmed
        Assert.Equal("~/" + _name, Assert.Single(d.Detect));   // the home folder is written as ~
        Assert.Equal([$"~/{_name}/skills:User", ".myapp/skills:Project"], d.Skills.Select(s => s.Path + ":" + s.Level));
        Assert.Equal((".myapp/mcp.json", "Project", "mcp"), (d.Configs[0].Path, d.Configs[0].Level, d.Configs[0].Kind));
        Assert.Equal(".myapp/commands", Assert.Single(d.Commands).Path);
        Assert.Equal("~/x/agents", Assert.Single(d.Agents).Path);   // the empty one is left out
        Assert.Equal(("~/x/logs", "text-log"), (d.Logs[0].Path, d.Logs[0].Format));
    }
}
