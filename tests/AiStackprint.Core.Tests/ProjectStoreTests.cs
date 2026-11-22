// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("projects-").FullName;
    private string File => Path.Combine(_dir, "projects.json");
    private string Home => Path.Combine(_dir, "home");

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Saved_projects_come_back_in_order_with_their_added_dates()
    {
        var a = new SavedProject(Path.Combine(Home, "code", "a"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var b = new SavedProject(Path.Combine(_dir, "elsewhere"), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        ProjectStore.Save([a, b], File, Home);
        Assert.Equal([a, b], ProjectStore.Load(File, Home));
    }

    [Fact]
    public void A_path_under_home_is_written_with_a_tilde_and_forward_slashes()
    {
        ProjectStore.Save([new(Path.Combine(Home, "code", "a"), DateTime.UtcNow)], File, Home);
        var json = JsonNode.Parse(System.IO.File.ReadAllText(File))!;
        Assert.Equal(1, (int)json["version"]!);
        Assert.Equal("~/code/a", (string)json["projects"]![0]!["path"]!);
    }

    [Theory]
    [InlineData("/home/u/code/a", "/home/u", "~/code/a")]
    [InlineData("/home/u/", "/home/u", "~")]
    [InlineData("/home/uv/code", "/home/u", "/home/uv/code")]   // a folder whose name only starts like home
    [InlineData("/srv/x", "/home/u", "/srv/x")]
    [InlineData(@"C:\Users\u\code\a", @"C:\Users\u", "~/code/a")]
    [InlineData(@"D:\work", @"C:\Users\u", @"D:\work")]
    public void Home_is_shortened_on_each_system(string path, string home, string written) =>
        Assert.Equal(written, ProjectStore.ToFile(path, home));

    [Theory]
    [InlineData("~/code/a", "/home/u", '/', "/home/u/code/a")]
    [InlineData("~/code/a", @"C:\Users\u", '\\', @"C:\Users\u\code\a")]
    [InlineData("~", "/home/u", '/', "/home/u")]
    [InlineData("/srv/x", "/home/u", '/', "/srv/x")]
    public void A_tilde_path_is_read_with_this_systems_home_and_separator(string written, string home, char sep, string path) =>
        Assert.Equal(path, ProjectStore.FromFile(written, home, sep));

    [Fact]
    public void Duplicates_and_bad_entries_are_dropped()
    {
        System.IO.File.WriteAllText(File, """
            { "version": 1, "projects": [
              { "path": "~/a", "added": "2026-01-01T00:00:00Z" },
              { "path": "~/a/" },
              { "path": "" },
              { "nope": 1 },
              "text",
              { "path": "~/b" }
            ] }
            """);
        var list = ProjectStore.Load(File, Home);
        Assert.Equal([Path.Combine(Home, "a"), Path.Combine(Home, "b")], list.Select(p => p.Path));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), list[0].Added);
    }

    [Fact]
    public void A_missing_folder_stays_in_the_list()
    {
        var gone = Path.Combine(_dir, "gone");
        ProjectStore.Save([new(gone, DateTime.UtcNow)], File, Home);
        Assert.False(Directory.Exists(gone));
        Assert.Equal(gone, Assert.Single(ProjectStore.Load(File, Home)).Path);
    }

    [Fact]
    public void No_file_or_a_broken_one_gives_an_empty_list()
    {
        Assert.Empty(ProjectStore.Load(File, Home));
        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Empty(ProjectStore.Load(File, Home));
        Assert.True(System.IO.File.Exists(Path.Combine(_dir, "backups", "projects.broken.json")));
    }

    [Fact]
    public void A_list_that_cannot_be_written_is_kept_for_this_run_only_and_the_data_folder_file_is_the_default()
    {
        System.IO.File.WriteAllText(File, "a file, not a folder");
        ProjectStore.Save([new SavedProject(Path.Combine(_dir, "p"), DateTime.UtcNow)], Path.Combine(File, "projects.json"));   // no error
        Assert.Empty(ProjectStore.Load(Path.Combine(File, "projects.json")));
        Assert.Equal(Path.Combine(DataFolder.Root, "projects.json"), ProjectStore.File);
        Assert.Null(ProjectStore.Problem);
        _ = ProjectStore.Load();   // the test home's list: read without an error
    }
}
