// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class AiFoldersTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aifold-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void Lists_existing_folders_with_apps_counts_and_size()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, ".app", "commands"));
        Directory.CreateDirectory(Path.Combine(_tmp, ".other"));
        File.WriteAllText(Path.Combine(_tmp, ".app", "commands", "a.md"), "12345");
        File.WriteAllText(Path.Combine(_tmp, ".app", "notes.txt"), "123");
        var d = new AppDefinition
        {
            Id = "app",
            Name = "App",
            Parts = { new PartSource
            {
                Path = ".app/commands",
                Level = "Project"
            } },
            Configs = { new ConfigFile
            {
                Path = "RULES.md",
                Level = "Project"
            } }
        };
        var d2 = new AppDefinition
        {
            Id = "o",
            Name = "Other",
            ProjectRoots = { new ScanRoot
            {
                Path = ".missing/skills",
                Level = "Project"
            } }
        };
        var items = new[] { new Item("Command", "a", "", "App", "Project", Path.Combine(_tmp, ".app", "commands", "a.md")) { Project = _tmp } };
        var f = Assert.Single(AiFolders.List(_tmp, [d, d2], items));
        Assert.Equal(".app", f.Name);
        Assert.Equal(["App"], f.Apps);
        Assert.Equal(1, f.Counts["Command"]);
        Assert.Equal(2, f.Files);
        Assert.Equal(8, f.Bytes);
    }
}
