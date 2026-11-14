// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ClaudeSettingsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiclaude-").FullName;
    private string Home => Path.Combine(_tmp, "home");
    private string Project => Path.Combine(_tmp, "repo");
    public void Dispose() => Directory.Delete(_tmp, true);

    private static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private static readonly AppDefinition _claude = new()
    {
        Id = "claude-code",
        Name = "Claude Code",
        Configs =
        {
            new ConfigFile { Path = "~/.claude/settings.json", Level = "User", Kind = "settings" },
            new ConfigFile { Path = ".claude/settings.json", Level = "Project", Kind = "settings" },
            new ConfigFile { Path = ".claude/settings.local.json", Level = "Local", Kind = "settings" }
        }
    };

    [Fact]
    public void Deny_wins_the_nearest_level_sets_single_values_and_env_shows_names_only()
    {
        Write(Path.Combine(Home, ".claude", "settings.json"), "{\"model\":\"opus\",\"permissions\":{\"allow\":[\"Bash(git:*)\",\"Read\"]},\"env\":{\"API_KEY\":\"sk-abcdefghijklmnopqrstuvwxyz123456\"}}");
        Write(Path.Combine(Project, ".claude", "settings.json"), "{\"model\":\"sonnet\",\"permissions\":{\"deny\":[\"Bash(git:*)\"],\"allow\":[\"Read\"]}}");
        Write(Path.Combine(Project, ".claude", "settings.local.json"), "{\"permissions\":{\"defaultMode\":\"acceptEdits\"}}");
        var info = ClaudeSettings.Read([_claude], Project, Home);
        Assert.Contains(info.Rules, r => r.Effect == "allow" && r.Rule == "Bash(git:*)" && r.Note == "Never applies: denied in Project");
        Assert.Contains(info.Rules, r => r.Effect == "allow" && r.Rule == "Read" && r.Level == "User" && r.Note!.StartsWith("Also set in", StringComparison.Ordinal));
        var model = Assert.Single(info.Settings, s => s.Key == "model");
        Assert.Equal("sonnet", model.Value);
        Assert.Equal("Project", model.Level);
        Assert.Contains("User: opus", model.Overrides);
        Assert.Equal("Local", Assert.Single(info.Settings, s => s.Key == "defaultMode").Level);
        var env = Assert.Single(info.Settings, s => s.Key == "env");
        Assert.Equal("API_KEY", env.Value);
    }

    [Fact]
    public void No_settings_files_gives_nothing()
    {
        Assert.False(ClaudeSettings.Read([_claude], Project, Home).Any);
    }

    [Theory]
    [InlineData("bash scripts/x.sh", "scripts/x.sh")]
    [InlineData("node \"$CLAUDE_PROJECT_DIR/.claude/hooks/a.js\" --flag", "/.claude/hooks/a.js")]
    [InlineData("python3 -m black .", null)]
    [InlineData("git status", null)]
    [InlineData("bash ./present.sh", null)]
    [InlineData("echo $HOME/x.sh", null)]
    public void Hook_scripts_are_found_and_checked(string command, string? missingEnd)
    {
        Write(Path.Combine(Project, "present.sh"), "x");
        var r = HookScript.Missing(command, Project, Home);
        if (missingEnd == null)
        {
            Assert.Null(r);
        }
        else
        {
            Assert.EndsWith(missingEnd, r!.Replace('\\', '/'));
        }
    }

    [Fact]
    public void A_project_hook_calling_a_missing_script_is_a_warning()
    {
        Write(Path.Combine(Project, ".claude", "settings.json"), "{\"hooks\":{\"PostToolUse\":[{\"matcher\":\"Edit\",\"hooks\":[{\"type\":\"command\",\"command\":\"bash scripts/lint.sh\"}]}]}}");
        var issue = Assert.Single(ProjectChecks.Run(Project, Project, [_claude], Home).Issues, i => i.Finding.Text.Contains("does not exist"));
        Assert.True(issue.Finding.Warning);
        Assert.Contains("PostToolUse · Edit", issue.Finding.Text);
        Assert.Contains("scripts/lint.sh", issue.Finding.Text);
    }
}
