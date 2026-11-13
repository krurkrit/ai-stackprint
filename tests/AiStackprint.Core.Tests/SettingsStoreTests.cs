using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Save_then_load_round_trips_and_filters_hidden_apps()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("aiprof-").FullName, "settings.json");
        SettingsStore.Save(new Settings { HiddenApps = { "warp" }, HideNotInstalled = true }, file);
        var s = SettingsStore.Load(file);
        Assert.True(s.HideNotInstalled);
        Assert.Equal(new[] { "warp" }, s.HiddenApps);
        var visible = SettingsStore.Visible(new[] { new AppDefinition { Id = "warp" }, new AppDefinition { Id = "zed" } }, s);
        Assert.Equal("zed", Assert.Single(visible).Id);
    }

    [Fact]
    public void Load_returns_defaults_for_missing_or_bad_file()
    {
        var dir = Directory.CreateTempSubdirectory("aiprof-").FullName;
        Assert.Empty(SettingsStore.Load(Path.Combine(dir, "none.json")).HiddenApps);
        var bad = Path.Combine(dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.False(SettingsStore.Load(bad).HideNotInstalled);
    }
}

public class AppCatalogTests
{
    [Fact]
    public void MigrateOldFolder_moves_old_data_once_and_never_overwrites()
    {
        var root = Directory.CreateTempSubdirectory("aiprof-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "AiProfiler"));
        File.WriteAllText(Path.Combine(root, "AiProfiler", "apps.json"), "[]");
        Assert.True(AppCatalog.MigrateOldFolder(root));
        Assert.True(File.Exists(Path.Combine(root, "AiStackprint", "apps.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "AiProfiler")));

        Directory.CreateDirectory(Path.Combine(root, "AiProfiler"));      // both exist: leave both alone
        Assert.False(AppCatalog.MigrateOldFolder(root));
        Assert.True(Directory.Exists(Path.Combine(root, "AiProfiler")));
    }
}
