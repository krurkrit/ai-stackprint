// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class McpServersTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;

    public void Dispose() => Directory.Delete(_tmp, true);

    private string Write(string name, string text)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void User_file_servers_and_per_project_servers_in_it()
    {
        var path = Write("claude.json", """
            { "numStartups": 3,
              "mcpServers": { "fs": { "command": "npx", "args": ["-y", "@x/fs@1.2.0"] } },
              "projects": { "C:/repo": { "mcpServers": { "db": { "type": "http", "url": "https://db.example.com/mcp", "headers": { "Authorization": "Bearer abcdefghijklmnopqrstuvwxyz" } } } } } }
            """);
        var item = new Item("Config", "claude.json", "MCP servers", "Claude Code", "User", path) { App = "claude-code", LevelLabel = "User and local MCP" };
        var servers = McpServers.Read([item, item with { Description = "Instructions" }]).ToDictionary(s => s.Name);

        Assert.Equal(2, servers.Count);
        var fs = servers["fs"];
        Assert.Equal(("User", false, "npx -y @x/fs@1.2.0"), (fs.Level, fs.Warn, fs.What));
        Assert.Null(fs.Project);
        Assert.Null(fs.Note);
        Assert.Equal(["claude-code"], fs.Apps);
        var db = servers["db"];
        Assert.Equal(("Local", "Key in plain text", true, "https://db.example.com/mcp"), (db.Level, db.Note, db.Warn, db.What));
        Assert.EndsWith("repo", db.Project);
    }

    [Fact]
    public void Codex_toml_servers_are_read()
    {
        var path = Write("config.toml", """
            model = "o3"
            [mcp_servers.docs]
            command = "npx"
            args = [
              "-y",   # comment, ] and [ inside
              "docs-mcp",
            ]
            env = { "DOCS_TOKEN" = "${DOCS_TOKEN}" }

            [mcp_servers."remote.one"]
            url = 'https://mcp.example.com'
            enabled = false

            [mcp_servers.git]
            command = "uvx"
            args = ["mcp-server-git==1.0"]
            [mcp_servers.git.env]
            GIT_TOKEN = "ghp_abcdefghijklmnopqrstuvwxyz0123456789"

            [profiles.x]
            command = "nope"
            """);
        var s = McpServers.InFile(path).ToDictionary(x => x.Name);

        Assert.Equal(["docs", "git", "remote.one"], s.Keys.Order());
        Assert.Equal(("npx -y docs-mcp", "Not pinned: docs-mcp · env: DOCS_TOKEN"), (s["docs"].What, s["docs"].Note));
        Assert.Equal(("https://mcp.example.com", "disabled"), (s["remote.one"].What, s["remote.one"].Note));
        Assert.Equal(("uvx mcp-server-git==1.0", "Key in plain text · env: GIT_TOKEN", true), (s["git"].What, s["git"].Note, s["git"].Warn));
        Assert.DoesNotContain(s.Values, x => (x.What + x.Note).Contains("ghp_"));
    }

    [Fact]
    public void Zed_opencode_and_vscode_formats()
    {
        var zed = McpServers.InFile(Write("settings.json", """
            { // Zed
              "context_servers": {
                "old": { "command": { "path": "node", "args": ["server.js"], "env": { "K": "v" } } },
                "new": { "command": "uvx", "args": ["zed-mcp"] },
              },
            }
            """)).ToDictionary(x => x.Name);
        Assert.Equal(("node server.js", "env: K"), (zed["old"].What, zed["old"].Note));
        Assert.Equal("Not pinned: zed-mcp", zed["new"].Note);

        var oc = Assert.Single(McpServers.InFile(Write("opencode.json", """
            { "mcp": { "local": { "type": "local", "command": ["bunx", "oc-mcp@2.0.1"], "environment": { "OC": "x" }, "enabled": false } } }
            """)));
        Assert.Equal(("bunx oc-mcp@2.0.1", "env: OC · disabled", false), (oc.What, oc.Note, oc.Warn));

        var vs = Assert.Single(McpServers.InFile(Write("mcp.json", """
            { "servers": { "gh": { "type": "http", "url": "https://api.githubcopilot.com/mcp/", "headers": { "Authorization": "Bearer ${input:token}" } } }, "inputs": [] }
            """)));
        Assert.Equal("https://api.githubcopilot.com/mcp/", vs.What);
        Assert.Null(vs.Note);
        Assert.Empty(McpServers.InFile(Write("broken.json", "{ nope")));

        var keyInPath = Assert.Single(McpServers.InFile(Write("cursor.json", """
            { "mcpServers": { "z": { "url": "https://mcp.example.com/s/4f9c2a7e1b3d4c5e6f7a8b9c0d1e2f3a/sse" } } }
            """)));
        Assert.Equal("https://mcp.example.com/s/" + ConfigJson.Hidden + "/sse", keyInPath.What);
    }
}
