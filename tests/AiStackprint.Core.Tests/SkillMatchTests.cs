// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class SkillMatchTests
{
    static Item Skill(string level, params string[] invokers) => new("Skill", "s", "", "App", level, "p") { Invokers = invokers };
    static readonly Item _agentOnly = Skill("User", "Agent"), _userOnly = Skill("User", "User"), _both = Skill("User", "Agent", "User");
    static readonly HashSet<string> _none = new();

    static string[] Shown(string invoked) =>
        new[] { _agentOnly, _userOnly, _both }.Where(i => SkillMatch.Shows(i, _none, invoked, "")).Select(SkillMatch.Label).ToArray();

    [Fact]
    public void Invoked_by_is_one_choice_matched_exactly()
    {
        Assert.Equal(new[] { "Agent only", "User only", "Agent and User" }, Shown(""));   // All
        Assert.Equal(new[] { "Agent only" }, Shown("Agent only"));
        Assert.Equal(new[] { "User only" }, Shown("User only"));
        Assert.Equal(new[] { "Agent and User" }, Shown("Agent and User"));
    }

    [Fact]
    public void Levels_filter_by_unchecked_levels()
    {
        Assert.False(SkillMatch.Shows(Skill("Project", "Agent"), new HashSet<string> { "Project" }, "", ""));
        Assert.True(SkillMatch.Shows(Skill("User", "Agent"), new HashSet<string> { "Project", "Local" }, "", ""));
        Assert.Equal("", SkillMatch.Label(new Item("Plugin", "p", "", "App", "User", "x")));
    }
}
