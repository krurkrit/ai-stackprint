// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The skill format checks and the checks over a whole scan.</summary>
public sealed class HealthChecksTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aihealth-").FullName;

    public void Dispose() => Directory.Delete(_tmp, true);

    private string Skill(string folder, string text)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp, folder)).FullName;
        var path = Path.Combine(dir, "SKILL.md");
        File.WriteAllText(path, text);
        return path;
    }

    private static List<string> Findings(string path) => [.. SkillCheck.Check(path, SkillDoc.Read(path)).Select(f => f.Text)];

    [Fact]
    public void A_skill_without_front_matter_or_with_it_left_open_is_told_so()
    {
        Assert.Contains(Findings(Skill("plain", "# Just text\n")), f => f.StartsWith("No front matter", StringComparison.Ordinal));
        Assert.Contains(Findings(Skill("open", "---\nname: open\n")), f => f.Contains("not closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Name_and_description_are_checked_one_by_one()
    {
        Assert.Contains("No name in the front matter.", Findings(Skill("noname", "---\ndescription: d\n---\nBody\n")));
        var bad = Findings(Skill("Folder", "---\nname: Bad_Name\ndescription: d\n---\nBody\n"));
        Assert.Contains(bad, f => f.StartsWith("Name should use only lowercase", StringComparison.Ordinal));
        Assert.Contains(bad, f => f.Contains("differs from its folder", StringComparison.Ordinal));

        var longName = new string('a', SkillCheck.NameMax + 1);
        Assert.Contains(Findings(Skill("long-name", $"---\nname: {longName}\ndescription: d\n---\nBody\n")), f => f.StartsWith($"Name is {longName.Length} characters", StringComparison.Ordinal));

        Assert.Contains("No description: the agent cannot tell when to use it.", Findings(Skill("nodesc", "---\nname: nodesc\n---\nBody\n")));
        var longDesc = new string('d', SkillCheck.DescriptionMax + 1);
        Assert.Contains(Findings(Skill("longdesc", $"---\nname: longdesc\ndescription: {longDesc}\n---\nBody\n")), f => f.StartsWith("Description is", StringComparison.Ordinal));
        var longCompat = new string('c', SkillCheck.CompatibilityMax + 1);
        Assert.Contains(Findings(Skill("compat", $"---\nname: compat\ndescription: d\ncompatibility: {longCompat}\n---\nBody\n")), f => f.StartsWith("Compatibility is over", StringComparison.Ordinal));
        Assert.Empty(Findings(Skill("fine", "---\nname: fine\ndescription: Does a thing\n---\nBody\n")));
    }

    [Fact]
    public void A_long_skill_file_is_advised_to_move_detail_out()
    {
        var body = string.Concat(Enumerable.Repeat("line\n", SkillCheck.BodyLines + 5));
        Assert.Contains(Findings(Skill("huge", "---\nname: huge\ndescription: d\n---\n" + body)), f => f.Contains("move detail into reference files", StringComparison.Ordinal));
    }

    [Fact]
    public void The_scan_is_checked_for_unreadable_skills_repeats_big_lists_and_plugin_problems()
    {
        var good = Skill("good", "---\nname: good\ndescription: ok\n---\nBody\n");
        var items = new List<Item>
        {
            new("Skill", "gone", "", "Claude Code", "User", Path.Combine(_tmp, "missing", "SKILL.md")) { App = "claude-code" },
            new("Skill", "good", "ok", "Claude Code", "User", good) { App = "claude-code", Invokers = ["Agent"] },
            new("Skill", "good", "ok", "Claude Code", "Project", good) { App = "claude-code", Invokers = ["Agent"] },
        };
        for (var i = 0; i < 60; i++)
        {
            items.Add(new("Skill", "skill-" + i, new string('x', 300), "Claude Code", "User", good) { App = "claude-code", Invokers = ["Agent"] });
        }

        var withHooks = Directory.CreateDirectory(Path.Combine(_tmp, "plug-hooks")).FullName;
        Directory.CreateDirectory(Path.Combine(withHooks, "hooks"));
        File.WriteAllText(Path.Combine(withHooks, "hooks", "hooks.json"), "{}");
        File.WriteAllText(Path.Combine(withHooks, ".mcp.json"), "{}");
        items.Add(new("Plugin", "runs-things", "", "Claude Code", "User", withHooks) { App = "claude-code" });
        items.Add(new("Plugin", "missing", "", "Claude Code", "User", Path.Combine(_tmp, "nope")) { App = "claude-code" });
        items.Add(new("Plugin", "nopath", "", "Claude Code", "User", "") { App = "claude-code" });
        items.Add(new("Plugin", "twice", "", "Claude Code", "User", withHooks) { App = "claude-code", Scope = "user" });
        items.Add(new("Plugin", "twice", "", "Claude Code", "Project", withHooks) { App = "claude-code", Scope = "project" });

        var all = Health.Compute(items).Select(h => h.Finding.Text).ToList();
        Assert.Contains(all, t => t.StartsWith("Cannot read:", StringComparison.Ordinal));
        Assert.Contains(all, t => t.StartsWith("The same name is at", StringComparison.Ordinal));
        Assert.Contains(all, t => t.Contains("of names and descriptions", StringComparison.Ordinal));
        Assert.Contains(all, t => t.StartsWith("Has hooks", StringComparison.Ordinal));
        Assert.Contains(all, t => t.StartsWith("Starts MCP servers", StringComparison.Ordinal));
        Assert.Contains(all, t => t.StartsWith("Listed as installed, but its folder is missing: " + Path.Combine(_tmp, "nope"), StringComparison.Ordinal));
        Assert.Contains(all, t => t.EndsWith("(no path)", StringComparison.Ordinal));
        Assert.Contains(all, t => t.StartsWith("Installed in 2 scopes: user, project", StringComparison.Ordinal));
        Assert.Contains(Health.Compute(items, "claude-opus-5-5").Select(h => h.Finding.Text), t => t.Contains("window", StringComparison.Ordinal) || t.Contains("of names and descriptions", StringComparison.Ordinal) || t.StartsWith("Cannot", StringComparison.Ordinal));
    }
}
