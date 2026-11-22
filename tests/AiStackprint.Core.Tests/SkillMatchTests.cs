// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class SkillMatchTests
{
    private static Item Skill(params string[] invokers) => new("Skill", "s", "", "App", "User", "p") { Invokers = invokers };

    [Fact]
    public void Invoked_by_labels_each_combination_and_plugins_have_none()
    {
        Assert.Equal("Agent only", SkillMatch.Label(Skill("Agent")));
        Assert.Equal("User only", SkillMatch.Label(Skill("User")));
        Assert.Equal("Agent and User", SkillMatch.Label(Skill("Agent", "User")));
        Assert.Equal("", SkillMatch.Label(new Item("Plugin", "p", "", "App", "User", "x")));
    }

    [Fact]
    public void From_is_the_plugin_the_app_or_a_folder()
    {
        Assert.Equal("git-helper", SkillMatch.From(Skill("Agent") with { Plugin = "git-helper@market" }));
        Assert.Equal("Bundled", SkillMatch.From(Skill("Agent") with { Bundled = true }));
        Assert.Equal("Folder", SkillMatch.From(Skill("Agent")));
    }
}
