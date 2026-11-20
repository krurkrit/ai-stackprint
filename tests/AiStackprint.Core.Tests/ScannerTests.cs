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
        Scanner.ReadRoot(root, new ScanRoot(), "App", rows);
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
            ProjectRoots = { new ScanRoot
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
            Roots = { new ScanRoot { Path = home }, new ScanRoot
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
}
