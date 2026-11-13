using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Tests;

public class SkillMatchTests
{
    static Item Skill(string level, params string[] invokers) => new("Skill", "s", "", "App", level, "p") { Invokers = invokers };
    static readonly Item AgentOnly = Skill("User", "Agent"), UserOnly = Skill("User", "User"), Both = Skill("User", "Agent", "User");
    static readonly HashSet<string> None = new();

    static string[] Shown(string invoked) =>
        new[] { AgentOnly, UserOnly, Both }.Where(i => SkillMatch.Shows(i, None, invoked, "")).Select(SkillMatch.Label).ToArray();

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
