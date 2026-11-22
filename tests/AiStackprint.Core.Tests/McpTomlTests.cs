// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Codex's config.toml: the values MCP servers use, read into the same JSON as other apps' servers.</summary>
public class McpTomlTests
{
    private static JsonNode Servers(string toml) => JsonNode.Parse(McpServers.Toml(toml))!["mcpServers"]!;

    [Fact]
    public void Strings_arrays_inline_tables_and_plain_values_are_read()
    {
        var s = Servers("""
            [mcp_servers.a]
            command = "npx"   # a comment
            args = [
              "-y", 'lit\n', "esc\t\"q\"",   # a trailing comma
            ]
            env = { KEY = "v", nested.deep = true, }
            enabled = false
            timeout = 1.5
            mode = fast
            x = 1
            x.y = 2
            [other]
            k = "skipped"
            [[mcp_servers.list]]
            k = "skipped too"
            [mcp_servers.b]
            args = ["z"
            """);
        var a = s["a"]!;
        Assert.Equal("npx", (string?)a["command"]);
        Assert.Equal(["-y", "lit\\n", "esc\t\"q\""], a["args"]!.AsArray().Select(v => (string?)v));
        Assert.Equal("v", (string?)a["env"]!["KEY"]);
        Assert.True((bool)a["env"]!["nested"]!["deep"]!);
        Assert.False((bool)a["enabled"]!);
        Assert.Equal(1.5, (double)a["timeout"]!);
        Assert.Equal("fast", (string?)a["mode"]);
        Assert.Equal(1, (double)a["x"]!);   // x.y cannot go inside a value
        Assert.Null(s["list"]);
        Assert.Equal(["z"], s["b"]!["args"]!.AsArray().Select(v => (string?)v));   // an array the file ends inside
        Assert.Equal(2, s.AsObject().Count);
    }

    [Fact]
    public void A_line_without_a_value_or_an_empty_key_is_skipped()
    {
        var a = Servers("[mcp_servers.a]\ncommand =\nnot a pair\n\"\" = \"empty key\"\nenv = { broken }\n")["a"]!.AsObject();
        Assert.False(a.ContainsKey("command"));
        Assert.Empty(a["env"]!.AsObject());
    }
}
