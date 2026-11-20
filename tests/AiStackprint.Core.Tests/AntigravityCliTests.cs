// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class AntigravityCliTests
{
    [Fact]
    public void Antigravity_CLI_is_found_by_its_own_folder_the_shared_config_folder_or_the_launcher_folder_and_its_files_are_listed()
    {
        var d = AppCatalog.Read().Defs.Single(a => a.Id == "antigravity-cli");
        Assert.Equal("agent", d.Category);
        Assert.Contains("~/.gemini/antigravity-cli", d.Detect);
        Assert.Contains("~/.gemini/config", d.Detect);   // where the CLI keeps mcp_config.json, plugins and projects
        Assert.Contains("%APPDATA%/Antigravity/bin", d.Detect);   // the agy launcher the Antigravity app installs on Windows
        Assert.All(d.Detect, p => Assert.False(p.EndsWith(".cmd", StringComparison.Ordinal) || p.EndsWith(".exe", StringComparison.Ordinal), $"{p}: detection looks for folders"));
        Assert.Contains(d.Roots, r => r.Path == "~/.gemini/config/skills");
        Assert.Contains(d.ProjectRoots, r => r.Path == ".agents/skills");
        Assert.Contains(d.Configs, c => c.Path == "~/.gemini/config/mcp_config.json" && c.Kind == "mcp");
        Assert.Contains(d.Logs, l => l.Pattern == "transcript.jsonl");
        Assert.Contains(d.Configs, c => c.Path == "~/.gemini/config/hooks.json" && c.Kind == "hooks");
        Assert.Contains(d.Configs, c => c.Path == ".agents/hooks.json" && c.Kind == "hooks" && c.Level == "Project");
    }

    [Fact]
    public void Hooks_json_is_read_by_name_and_event_with_matcher_groups_plain_lists_and_the_enabled_flag()
    {
        var home = Directory.CreateTempSubdirectory("agy-hooks").FullName;
        try
        {
            var file = Path.Combine(home, "hooks.json");
            File.WriteAllText(file, """
                {
                  "lint": { "PostToolUse": [ { "matcher": "run_command", "hooks": [ { "type": "command", "command": "echo lint", "timeout": 10 } ] } ] },
                  "guard": { "enabled": false, "PreToolUse": [ { "matcher": "", "hooks": [ { "command": "echo guard" } ] } ] },
                  "context": { "PreInvocation": [ { "type": "command", "command": "echo ctx" } ], "SessionStart": [ { "command": "echo start" } ] }
                }
                """);
            var hooks = Hooks.InFile(file, home, home);
            Assert.Equal(4, hooks.Count);
            var lint = hooks.Single(h => h.Event == "PostToolUse");
            Assert.Equal("run_command", lint.Matcher);
            Assert.Equal("echo lint", lint.What);
            Assert.Contains("hook lint", lint.Note);
            Assert.Contains("switched off", hooks.Single(h => h.Event == "PreToolUse").Note);
            Assert.Equal(["PreInvocation", "SessionStart"], hooks.Where(h => h.Matcher == null).Select(h => h.Event).Order());
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void A_settings_style_hooks_file_is_still_read_the_old_way()
    {
        var home = Directory.CreateTempSubdirectory("agy-hooks2").FullName;
        try
        {
            var file = Path.Combine(home, "settings.json");
            File.WriteAllText(file, """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo done" } ] } ] } }""");
            Assert.Equal("Stop", Assert.Single(Hooks.InFile(file, home, home)).Event);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void An_app_with_one_of_its_folders_on_disk_is_installed_and_listed_with_its_instruction_and_mcp_files()
    {
        var home = Directory.CreateTempSubdirectory("agy-home").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(home, ".gemini", "config"));
            File.WriteAllText(Path.Combine(home, ".gemini", "config", "mcp_config.json"), "{}");
            var def = new AiStackprint.Models.AppDefinition { Id = "agy-test", Name = "Agy test", Category = "agent" };
            def.Detect.Add(Path.Combine(home, ".gemini", "antigravity-cli"));
            def.Detect.Add(Path.Combine(home, ".gemini", "config"));
            var r = Scanner.Collect([def], []);
            var app = Assert.Single(r.Apps);
            Assert.True(app.Installed);
            Assert.EndsWith("config", app.Folder);
        }
        finally { Directory.Delete(home, true); }
    }
}
