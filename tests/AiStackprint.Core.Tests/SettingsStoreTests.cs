// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
        var visible = SettingsStore.Visible([new AppDefinition { Id = "warp" }, new AppDefinition { Id = "zed" }], s);
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
