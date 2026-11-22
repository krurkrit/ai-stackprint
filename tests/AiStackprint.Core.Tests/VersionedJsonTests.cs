// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class VersionedJsonTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("versioned-").FullName;
    private string File => Path.Combine(_dir, "thing.json");
    private string Backups => Path.Combine(_dir, "backups");

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>v1 → v2 renames "theme" to "look"; v2 → v3 moves it into "appearance".</summary>
    private static VersionedJson Format() => new("thing", 3,
        v1 =>
        {
            if (v1.Remove("theme", out var t)) { v1["look"] = t; }
            return v1;
        },
        v2 =>
        {
            if (v2.Remove("look", out var l)) { v2["appearance"] = new JsonObject { ["theme"] = l }; }
            return v2;
        });

    [Fact]
    public void An_old_file_is_upgraded_step_by_step_backed_up_and_written_back()
    {
        System.IO.File.WriteAllText(File, """
            // a comment, and a trailing comma
            { "theme": "Dark", "fontSize": 16, }
            """);

        var data = Format().Read(File, Backups)!;

        Assert.Equal("""{"fontSize":16,"appearance":{"theme":"Dark"}}""", data.ToJsonString());
        Assert.Equal("""{"version":3,"fontSize":16,"appearance":{"theme":"Dark"}}""", JsonNode.Parse(System.IO.File.ReadAllText(File))!.ToJsonString());
        Assert.Contains("\"theme\": \"Dark\"", System.IO.File.ReadAllText(Path.Combine(Backups, "thing.v1.json")));
    }

    [Fact]
    public void A_v2_file_takes_only_the_last_step()
    {
        System.IO.File.WriteAllText(File, """{ "version": 2, "look": "Light" }""");
        Assert.Equal("""{"appearance":{"theme":"Light"}}""", Format().Read(File, Backups)!.ToJsonString());
        Assert.True(System.IO.File.Exists(Path.Combine(Backups, "thing.v2.json")));
    }

    [Fact]
    public void A_current_file_is_read_as_it_is_and_not_backed_up()
    {
        System.IO.File.WriteAllText(File, """{ "version": 3, "a": 1 }""");
        Assert.Equal("""{"a":1}""", Format().Read(File, Backups)!.ToJsonString());
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Write_puts_the_version_first_and_leaves_no_temporary_file()
    {
        var f = Format();
        f.Write(File, new JsonObject
        {
            ["b"] = 2,
            ["version"] = 1
        });
        Assert.Equal("""{"version":3,"b":2}""", JsonNode.Parse(System.IO.File.ReadAllText(File))!.ToJsonString());
        Assert.Equal(["thing.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Merge_puts_changes_over_defaults_and_Diff_keeps_only_what_differs()
    {
        var defaults = JsonNode.Parse("""{ "a": 1, "list": [1], "o": { "x": 1, "y": 2 } }""")!.AsObject();
        var changes = JsonNode.Parse("""{ "a": 5, "o": { "y": 3 }, "new": true }""")!.AsObject();
        var merged = JsonLayers.Merge(defaults, changes);
        Assert.Equal("""{"a":5,"list":[1],"o":{"x":1,"y":3},"new":true}""", merged.ToJsonString());
        Assert.Equal(changes.ToJsonString(), JsonLayers.Diff(merged, defaults).ToJsonString());
        Assert.Equal("{}", JsonLayers.Diff(defaults, defaults).ToJsonString());
    }

    [Fact]
    public void A_bare_list_is_version_1_under_its_key_and_another_value_is_kept_as_broken_each_time()
    {
        var f = new VersionedJson("list", 1) { ListKey = "apps" };
        Assert.Equal(1, f.Current);
        System.IO.File.WriteAllText(File, """[{ "id": "a" }]""");
        Assert.Equal("a", (string?)f.Read(File, Backups)!["apps"]![0]!["id"]);

        System.IO.File.WriteAllText(File, "42");
        Assert.Null(f.Read(File, Backups));
        Assert.Null(f.Read(File, Backups));   // a second time: a second copy, with a time in its name
        Assert.Equal(2, Directory.GetFiles(Backups, "list.broken*.json").Length);
        Assert.Null(f.Read(Path.Combine(_dir, "none.json"), Backups));
    }
}
