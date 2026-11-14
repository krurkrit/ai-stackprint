// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ChangeAlertsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aialert-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);
    private string Snapshot => Path.Combine(_tmp, "last-scan.json");

    private Item Skill(string name = "s", string body = "one")
    {
        var dir = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.Combine(dir, "scripts"));
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: " + name + "\ndescription: d\n---\n" + body);
        return new Item("Skill", name, "d", "App", "User", Path.Combine(dir, "SKILL.md")) { App = "app" };
    }

    [Fact]
    public void An_edited_file_in_a_skill_folder_is_a_change_but_a_new_time_alone_is_not()
    {
        var s = Skill();
        ScanDiff.Update([s], Snapshot);
        File.SetLastWriteTimeUtc(s.Path, DateTime.UtcNow.AddMinutes(5));
        Assert.Empty(ScanDiff.Update([s], Snapshot).Changes);   // same content, new time

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.Path)!, "scripts", "run.sh"), "echo hi");
        var c = Assert.Single(ScanDiff.Update([s], Snapshot).Changes);
        Assert.Equal(("Edited", "Skill", "files changed"), (c.What, c.Kind, c.Detail));
        Assert.Empty(ScanDiff.Update([s], Snapshot).Changes);
    }

    [Fact]
    public void A_plugin_version_bump_names_both_versions()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp, "plug")).FullName;
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{}");
        var p = new Item("Plugin", "plug", "d", "App", "User", dir) { App = "app", Provides = ["skills"], Version = "1.2.0" };
        ScanDiff.Update([p], Snapshot);
        var c = Assert.Single(ScanDiff.Update([p with { Version = "1.3.0" }], Snapshot).Changes);
        Assert.Equal("version 1.2.0 → 1.3.0", c.Detail);
    }

    [Fact]
    public void An_MCP_server_whose_definition_changed_is_edited_and_the_snapshot_holds_no_secret()
    {
        static string Def(string cmd, string key) => $"{{\"mcpServers\":{{\"x\":{{\"command\":\"{cmd}\",\"env\":{{\"API_KEY\":\"{key}\"}}}}}}}}";
        var file = Path.Combine(_tmp, "mcp.json");
        var cfg = new Item("Config", "mcp.json", "MCP servers", "App", "User", file) { App = "app" };
        File.WriteAllText(file, Def("node", "sk-abcdefghijklmnopqrstuvwxyz123456"));
        ScanDiff.Update([cfg], Snapshot, McpServers.Read([cfg]));
        Assert.DoesNotContain("sk-abc", File.ReadAllText(Snapshot));
        Assert.Empty(ScanDiff.Update([cfg], Snapshot, McpServers.Read([cfg])).Changes);

        File.WriteAllText(file, Def("evil", "sk-abcdefghijklmnopqrstuvwxyz123456"));
        var servers = McpServers.Read([cfg]);
        var c = Assert.Single(ScanDiff.Update([cfg], Snapshot, servers).Changes, x => x.Kind == "MCP server");
        Assert.Equal(("Edited", "x", "command or address changed"), (c.What, c.Name, c.Detail));
        Assert.DoesNotContain("evil", File.ReadAllText(Snapshot));
    }

    [Fact]
    public void An_older_snapshot_without_hashes_gives_no_change()
    {
        var s = Skill();
        File.WriteAllText(Snapshot, $"{{\"Time\":\"2026-01-01T00:00:00\",\"Items\":[{{\"Kind\":\"Skill\",\"Name\":\"s\",\"App\":\"app\",\"Path\":{System.Text.Json.JsonSerializer.Serialize(s.Path)},\"Stamp\":0}}]}}");
        Assert.Empty(ScanDiff.Update([s], Snapshot).Changes);
    }
}
