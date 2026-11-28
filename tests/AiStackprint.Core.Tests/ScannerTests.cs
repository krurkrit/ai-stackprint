// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private static void Skill(string dir, string name, string extra = "")
    {
        Directory.CreateDirectory(Path.Combine(dir, name));
        File.WriteAllText(Path.Combine(dir, name, "SKILL.md"), $"---\nname: {name}\ndescription: d\n{extra}---\nbody\n");
    }

    [Fact]
    public void Reads_invocation_flags_from_front_matter()
    {
        var root = Path.Combine(_tmp, "skills");
        Skill(root, "both");
        Skill(root, "manual", "disable-model-invocation: true\n");
        Skill(root, "auto", "user-invocable: false\n");
        var rows = new List<Item>();
        Scanner.ReadRoot(root, new SkillSource(), "App", rows);
        var by = rows.ToDictionary(r => r.Name, SkillMatch.Label);
        Assert.Equal("Agent and User", by["both"]);
        Assert.Equal("User only", by["manual"]);
        Assert.Equal("Agent only", by["auto"]);
    }

    [Fact]
    public void Nested_project_roots_and_files_are_found_in_sub_folders_but_not_hidden_or_build_folders()
    {
        var proj = Path.Combine(_tmp, "repo");
        Skill(Path.Combine(proj, ".app", "skills"), "root-skill");
        Skill(Path.Combine(proj, "services", "api", ".app", "skills"), "api-skill");
        Skill(Path.Combine(proj, "node_modules", "x", ".app", "skills"), "ignored");
        Directory.CreateDirectory(Path.Combine(proj, "services", "api"));
        File.WriteAllText(Path.Combine(proj, "RULES.md"), "r");
        File.WriteAllText(Path.Combine(proj, "services", "api", "RULES.md"), "r");
        var d = new AppDefinition
        {
            Id = "app",
            Name = "App",
            Skills = { new SkillSource
            {
                Path = ".app/skills",
                Level = "Project",
                Nested = true,
                Label = "REPO"
            } },
            Configs = { new ConfigFile
            {
                Path = "RULES.md",
                Level = "Project",
                Nested = true
            }, new ConfigFile
            {
                Path = "LOCAL.md",
                Level = "Local"
            } }
        };
        File.WriteAllText(Path.Combine(proj, "LOCAL.md"), "l");
        var items = Scanner.Collect([d], [proj]).Items;
        var skills = items.Where(i => i.Kind == "Skill").ToList();
        Assert.Equal(["api-skill", "root-skill"], skills.Select(s => s.Name).Order());
        Assert.All(skills, s =>
        {
            Assert.Equal(proj, s.Project);
            Assert.Equal("REPO", s.LevelLabel);
        });
        Assert.Equal(2, items.Count(i => i.Kind == "Config" && i.Name == "RULES.md"));
        var local = Assert.Single(items, i => i.Name == "LOCAL.md");
        Assert.Equal("Local", local.Level);
        Assert.Equal(proj, local.Project);
    }

    [Fact]
    public void Bundled_root_marks_skills_bundled_even_when_a_broader_root_also_finds_them()
    {
        var home = Path.Combine(_tmp, "codex", "skills");
        Skill(Path.Combine(home, ".system"), "skill-creator");
        Skill(home, "mine");
        var d = new AppDefinition
        {
            Id = "codex",
            Name = "Codex",
            Skills = { new SkillSource { Path = home }, new SkillSource
            {
                Path = Path.Combine(home, ".system"),
                Bundled = true
            } }
        };
        var skills = Scanner.Collect([d], []).Items.Where(i => i.Kind == "Skill").ToList();
        Assert.True(Assert.Single(skills, s => s.Name == "skill-creator").Bundled);
        Assert.False(Assert.Single(skills, s => s.Name == "mine").Bundled);
        Assert.NotNull(SkillManager.CannotRemove(skills.First(s => s.Bundled)));
    }

    [Fact]
    public void User_level_sources_are_read_from_their_own_paths_and_project_plugins_from_each_project()
    {
        var user = Path.Combine(_tmp, "user");
        var proj = Path.Combine(_tmp, "proj");
        Directory.CreateDirectory(Path.Combine(user, "flat"));
        File.WriteAllText(Path.Combine(user, "flat", "tidy.md"), "---\nname: tidy\ndescription: d\n---\n");
        Directory.CreateDirectory(Path.Combine(user, "plugins"));
        File.WriteAllText(Path.Combine(user, "plugins", "notify.ts"), "");
        Directory.CreateDirectory(Path.Combine(proj, ".app", "plugins"));
        File.WriteAllText(Path.Combine(proj, ".app", "plugins", "local.js"), "");
        Directory.CreateDirectory(Path.Combine(user, "commands"));
        File.WriteAllText(Path.Combine(user, "commands", "review.md"), "---\ndescription: r\n---\n");
        File.WriteAllText(Path.Combine(user, "RULES.md"), "r");
        var d = new AppDefinition
        {
            Id = "app",
            Name = "App",
            Skills = { new SkillSource { Path = Path.Combine(user, "flat"), Level = "User", Layout = "flat-md" }, new SkillSource { Path = Path.Combine(user, "none"), Level = "User" } },
            PluginFormat = "claude",
            PluginManifest = Path.Combine(user, "no-manifest.json"),
            Plugins = { new PluginSource { Format = "opencode-files", Path = Path.Combine(user, "plugins") }, new PluginSource { Format = "opencode-files", Path = ".app/plugins", Level = "Project" } },
            Configs = { new ConfigFile { Path = Path.Combine(user, "RULES.md"), Level = "User" }, new ConfigFile { Path = Path.Combine(user, "MISSING.md"), Level = "User" } },
            Commands = { new PartSource { Path = Path.Combine(user, "commands") } }
        };
        var items = Scanner.Collect([d], [proj]).Items;
        Assert.Contains(items, i => i.Kind == "Skill" && i.Name == "tidy");
        Assert.Contains(items, i => i.Kind == "Plugin" && i.Name == "notify" && i.Project == null);
        Assert.Contains(items, i => i.Kind == "Plugin" && i.Name == "local" && i.Project == proj);
        Assert.Contains(items, i => i.Kind == "Config" && i.Name == "RULES.md" && i.Level == "User");
        Assert.DoesNotContain(items, i => i.Name == "MISSING.md");
        Assert.Contains(items, i => i.Name.Contains("review", StringComparison.Ordinal) && i.App == "app");
    }
}
