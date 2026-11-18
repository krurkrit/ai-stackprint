// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ProjectFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pfiles-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private void Touch(string rel)
    {
        var f = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "x");
    }

    [Fact]
    public void Files_are_listed_relative_with_slashes_and_dependency_and_build_folders_are_skipped()
    {
        foreach (var f in new[] { "README.md", "src/App/Program.cs", "docs/a/b/c.md", "node_modules/pkg/index.js", "bin/Debug/app.dll", ".git/config", "src/obj/x.cs" })
        {
            Touch(f);
        }

        ProjectFiles.Forget();
        var list = ProjectFiles.List(_dir, out var complete);
        Assert.True(complete);
        Assert.Equal(["README.md", "docs/a/b/c.md", "src/App/Program.cs"], list.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_list_stops_at_the_cap_and_says_it_is_not_complete()
    {
        for (var i = 0; i < 12; i++)
        {
            Touch($"d{i % 3}/f{i}.txt");
        }

        ProjectFiles.Forget();
        var list = ProjectFiles.List(_dir, out var complete, max: 5);
        Assert.False(complete);
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public void The_same_folder_is_walked_once_until_Forget()
    {
        Touch("a.txt");
        ProjectFiles.Forget();
        var first = ProjectFiles.List(_dir, out _);
        Touch("b.txt");
        Assert.Same(first, ProjectFiles.List(_dir, out _));   // kept for the checks of one scan
        ProjectFiles.Forget();
        Assert.Equal(2, ProjectFiles.List(_dir, out _).Count);
    }
}
