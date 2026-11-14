// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ScannerPartsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiparts-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text)
    {
        var f = Path.Combine(_tmp, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    [Fact]
    public void Commands_take_names_from_files_and_prefix_sub_folders()
    {
        Write("c/review.md", "---\ndescription: Review <b>code</b>\n---\nbody");
        Write("c/team/ship.md", "no front matter");
        var rows = new List<Item>();
        Scanner.ReadParts(Path.Combine(_tmp, "c"), new PartSource(), "App", rows);
        var by = rows.ToDictionary(r => r.Name);
        Assert.Equal(["review", "team:ship"], by.Keys.Order());
        Assert.All(rows, r => Assert.Equal("Command", r.Kind));
        Assert.DoesNotContain("<b>", by["review"].Description);
    }

    [Fact]
    public void Toml_commands_read_the_description_and_endings_are_stripped()
    {
        Write("g/fix.toml", "description = \"Fix it\"\nprompt = \"x\"\n");
        Write("p/a.prompt.md", "---\ndescription: A\n---\n");
        var rows = new List<Item>();
        Scanner.ReadParts(Path.Combine(_tmp, "g"), new PartSource { Pattern = "*.toml" }, "App", rows);
        Scanner.ReadParts(Path.Combine(_tmp, "p"), new PartSource { Pattern = "*.prompt.md" }, "App", rows);
        Assert.Equal("Fix it", rows.Single(r => r.Name == "fix").Description);
        Assert.Contains(rows, r => r.Name == "a");
    }

    [Fact]
    public void Agents_use_their_front_matter_name_and_projects_are_scanned()
    {
        Write("repo/.app/agents/x.md", "---\nname: reviewer\ndescription: Reads diffs\n---\n");
        var d = new AppDefinition { Id = "app", Name = "App", Parts = { new PartSource { Path = ".app/agents", Level = "Project", Kind = "agent" } } };
        var repo = Path.Combine(_tmp, "repo");
        var a = Assert.Single(Scanner.Collect([d], [repo]).Items, i => i.Kind == "Agent");
        Assert.Equal("reviewer", a.Name);
        Assert.Equal(repo, a.Project);
        Assert.Equal("app", a.App);
    }

    [Fact]
    public void Missing_folders_give_nothing()
    {
        var rows = new List<Item>();
        Scanner.ReadParts(Path.Combine(_tmp, "none"), new PartSource(), "App", rows);
        Assert.Empty(rows);
    }
}
