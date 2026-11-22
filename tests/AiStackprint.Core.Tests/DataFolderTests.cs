// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class DataFolderTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("datafolder-").FullName;
    private string Old => Path.Combine(_tmp, "AppData", "AiStackprint");
    private string Root => Path.Combine(_tmp, "home", ".ai-stackprint");

    public void Dispose() => Directory.Delete(_tmp, true);

    private static void Write(string folder, string rel, string text = "x")
    {
        var path = Path.Combine(folder, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Old_files_move_to_their_place_and_the_old_folder_keeps_a_note()
    {
        foreach (var f in new[] { "settings.json", "apps.json", "prices.json", "scan-cache.json", "last-scan.json", "crash.log", "crash.seen", "perf.log", "perf-report.txt",
            Path.Combine("backups", "20260101-120000-000", "settings.json"), Path.Combine("removed", "20260102-120000-000", "my-skill", "SKILL.md") })
        {
            Write(Old, f);
        }

        var r = DataFolder.Move([Old], Root);

        Assert.Equal(new DataFolder.MoveResult(11, 0, 0), r);
        foreach (var f in new[] { "settings.json", "apps.json", "prices.json", Path.Combine("cache", "scan-cache.json"), Path.Combine("cache", "last-scan.json"),
            Path.Combine("logs", "crash.log"), Path.Combine("logs", "crash.seen"), Path.Combine("logs", "perf.log"), Path.Combine("logs", "perf-report.txt"),
            Path.Combine("backups", "configs", "20260101-120000-000", "settings.json"), Path.Combine("backups", "skills", "20260102-120000-000", "my-skill", "SKILL.md") })
        {
            Assert.True(File.Exists(Path.Combine(Root, f)), f);
        }

        Assert.Equal([DataFolder.Note], Directory.GetFileSystemEntries(Old).Select(Path.GetFileName));   // emptied, sub-folders gone
        Assert.Contains("can be deleted", File.ReadAllText(Path.Combine(Old, DataFolder.Note)));
        Assert.Equal(new DataFolder.MoveResult(0, 0, 0), DataFolder.Move([Old], Root));   // run again: nothing to do, the note stays
    }

    [Fact]
    public void Nothing_to_move_creates_nothing()
    {
        Assert.Equal(new DataFolder.MoveResult(0, 0, 0), DataFolder.Move([Old, Path.Combine(_tmp, "AppData", "AiProfiler")], Root));
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void A_file_already_in_the_new_folder_is_never_overwritten()
    {
        Write(Root, "settings.json", "new");
        Write(Old, "settings.json", "old");
        Write(Old, "apps.json", "[]");

        Assert.Equal(new DataFolder.MoveResult(1, 1, 0), DataFolder.Move([Old], Root));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Root, "settings.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Old, "settings.json")));   // left for the user to compare
        Assert.Contains("settings.json", File.ReadAllText(Path.Combine(Old, DataFolder.Note)));
    }

    [Fact]
    public void A_file_that_cannot_be_copied_stays_and_is_counted()
    {
        Write(Old, "settings.json");
        Write(Old, "crash.log");
        Directory.CreateDirectory(Path.Combine(Root, "logs", "crash.log"));   // a folder where the file should go: the copy fails

        var r = DataFolder.Move([Old], Root);

        Assert.Equal((1, 1), (r.Moved, r.Failed));
        Assert.True(File.Exists(Path.Combine(Old, "crash.log")));
        Assert.True(File.Exists(Path.Combine(Root, "settings.json")));
    }

    [Fact]
    public void The_oldest_name_AiProfiler_moves_too()
    {
        var profiler = Path.Combine(_tmp, "AppData", "AiProfiler");
        Write(profiler, "apps.json", "[]");
        Assert.Equal(1, DataFolder.Move([Old, profiler], Root).Moved);
        Assert.True(File.Exists(Path.Combine(Root, "apps.json")));
    }

    [Theory]
    [InlineData("settings.json", "settings.json")]
    [InlineData("scan-cache.json", "cache/scan-cache.json")]
    [InlineData("perf.log", "logs/perf.log")]
    [InlineData("backups/x/a.json", "backups/configs/x/a.json")]
    [InlineData("removed/x/s/SKILL.md", "backups/skills/x/s/SKILL.md")]
    [InlineData("other/crash.log", "other/crash.log")]
    public void Each_old_file_has_a_place(string rel, string place) =>
        Assert.Equal(place.Replace('/', Path.DirectorySeparatorChar), DataFolder.Place(rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Prepare_moves_the_files_of_an_older_version_from_app_data_into_the_data_folder()
    {
        var name = $"moved-{Guid.NewGuid():N}.json";
        var old = Path.Combine(PathUtil.SystemFolder(Environment.SpecialFolder.ApplicationData), "AiProfiler");
        Write(old, name, "{}");
        try
        {
            DataFolder.Prepare();
            Assert.Null(DataFolder.MoveProblem);
            Assert.True(File.Exists(Path.Combine(DataFolder.Root, name)));
            Assert.False(File.Exists(Path.Combine(old, name)));
        }
        finally
        {
            File.Delete(Path.Combine(DataFolder.Root, name));
            Directory.Delete(old, true);
        }
    }

    [Fact]
    public void The_data_folders_are_inside_the_data_folder()
    {
        Assert.All(new[] { DataFolder.Cache, DataFolder.Logs, DataFolder.Backups, DataFolder.ConfigBackups, DataFolder.SkillBackups }, p => Assert.StartsWith(DataFolder.Root, p, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(DataFolder.Backups, "skills"), DataFolder.SkillBackups);
    }
}
