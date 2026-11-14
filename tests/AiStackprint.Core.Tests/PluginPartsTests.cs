// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class PluginPartsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text = "x")
    {
        var f = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    [Fact]
    public void Folders_in_their_default_places_count()
    {
        Write("skills/fmt/SKILL.md");
        Write("commands/deploy.md");
        Write("agents/readme.txt");          // not an agent file
        Write("hooks/hooks.json", "{}");
        Write(".mcp.json", "{}");
        Assert.Equal(["skills", "commands", "hooks", "mcp"], PluginParts.Find(_tmp, null));
    }

    [Fact]
    public void Parts_named_in_the_manifest_count_without_folders()
    {
        using var doc = JsonDocument.Parse("""{ "mcpServers": { "db": { "command": "x" } }, "commands": "./cmds", "agents": [], "lspServers": {}, "skills": "" }""");
        Assert.Equal(["commands", "mcp"], PluginParts.Find(_tmp, doc.RootElement));   // empty values do not count
        Assert.Empty(PluginParts.Find("", doc.RootElement));
        Assert.Equal("MCP servers", PluginParts.Name("mcp"));
    }

    [Fact]
    public void Plugin_rows_carry_their_parts_and_search_finds_them_with_has()
    {
        Write("plug/.claude-plugin/plugin.json", """{ "name": "p", "mcpServers": { "a": {} } }""");
        Write("plug/skills/one/SKILL.md", "---\nname: one\ndescription: d\n---\n");
        var esc = Path.Combine(_tmp, "plug").Replace("\\", "\\\\");
        Write("installed_plugins.json", $$"""{ "plugins": { "p@m": [ { "installPath": "{{esc}}" } ] } }""");
        var rows = new List<Item>();
        PluginReaders.Find("claude")!.Read(Path.Combine(_tmp, "installed_plugins.json"), new AppDefinition { Id = "claude-code", Name = "Claude Code" }, rows);
        var plugin = rows.Single(r => r.Kind == "Plugin");
        Assert.Equal(["skills", "mcp"], plugin.Provides);

        var search = new ItemSearch(rows);
        Assert.Equal([plugin], search.Find("has:mcp"));
        Assert.Equal([plugin], search.Find("has:\"MCP servers\""));
        Assert.Empty(search.Find("has:hooks"));
    }
}
