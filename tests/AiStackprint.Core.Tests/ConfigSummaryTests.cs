// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ConfigSummaryTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiholds-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private string Write(string name, string text)
    {
        var f = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
        return f;
    }

    private static Item Config(string path, string kind) => new("Config", Path.GetFileName(path), kind, "App", "User", path) { App = "app" };

    [Fact]
    public void A_settings_file_counts_its_servers_hooks_models_and_permission_rules()
    {
        var f = Write("settings.json", """
            { "model": "claude-opus-5-5", "env": { "SMALL_MODEL": "claude-haiku-5-5" },
              "permissions": { "allow": ["Read", "Edit"], "deny": ["Bash(rm:*)"] } }
            """);
        McpServer[] servers =
        [
            new("a", "x", f, null, false), new("b", "y", f, null, false), new("go", "gopls", f, null, false) { Kind = "LSP" },
            new("other", "z", Path.Combine(_tmp, "other.json"), null, false), new("plug", "p", f, null, false) { Plugin = "p@m" },
        ];
        Hook[] hooks = [new("Stop", null, "echo", "command", f)];

        var parts = ConfigSummary.Of(Config(f, "Settings"), servers, hooks);

        Assert.Equal(["2 MCP servers", "1 LSP server", "1 hook", "models claude-opus-5-5, claude-haiku-5-5", "3 permission rules"], parts.Select(p => p.Text));
        Assert.Equal(["Mcp", "Mcp", "Hooks", null, null], parts.Select(p => p.Page));
    }

    [Fact]
    public void An_ignore_file_counts_its_patterns_and_a_hook_folder_its_hooks()
    {
        var ignore = Write(".cursorignore", "# secrets\n.env\n\nnode_modules/\n");
        Assert.Equal(["2 patterns"], ConfigSummary.Of(Config(ignore, "Ignore"), [], []).Select(p => p.Text));

        var folder = Path.Combine(_tmp, "hooks");
        Hook[] hooks = [new("a", null, "x", "command", Path.Combine(folder, "one.json")), new("b", null, "x", "command", Path.Combine(folder, "two.json"))];
        Assert.Equal(["2 hooks"], ConfigSummary.Of(Config(folder, "Hooks"), [], hooks).Select(p => p.Text));
    }

    [Fact]
    public void A_toml_file_names_its_model_and_invalid_json_gives_only_counts()
    {
        var toml = Write("config.toml", "model = \"gpt-5.5\"\n[mcp_servers.x]\ncommand = \"x\"\n");
        Assert.Equal(["model gpt-5.5"], ConfigSummary.Of(Config(toml, "Settings"), [], []).Select(p => p.Text));

        var bad = Write("bad.json", "{ \"model\": ");
        Assert.Empty(ConfigSummary.Of(Config(bad, "Settings"), [], []));
    }
}
