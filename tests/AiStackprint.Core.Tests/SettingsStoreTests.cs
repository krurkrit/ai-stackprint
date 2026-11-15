// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("settings-").FullName;
    private string File => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, true);

    private JsonObject Saved() => JsonNode.Parse(System.IO.File.ReadAllText(File))!.AsObject();

    [Fact]
    public void Save_then_load_round_trips_and_filters_hidden_apps()
    {
        var st = SettingsStore.Defaults();
        st.HiddenApps.Add("warp");
        st.HideNotInstalled = true;
        SettingsStore.Save(st, File);
        var s = SettingsStore.Load(File);
        Assert.True(s.HideNotInstalled);
        Assert.Equal(new[] { "warp" }, s.HiddenApps);
        var visible = SettingsStore.Visible([new AppDefinition { Id = "warp" }, new AppDefinition { Id = "zed" }], s);
        Assert.Equal("zed", Assert.Single(visible).Id);
    }

    [Fact]
    public void Every_setting_has_a_default_in_default_settings_json()
    {
        var defaults = SettingsStore.DefaultJson();
        var names = typeof(Settings).GetProperties().Where(p => p.Name != nameof(Settings.Extra)).Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]);
        Assert.DoesNotContain(names, n => !defaults.ContainsKey(n));
        var d = SettingsStore.Defaults();
        Assert.Equal(("System", "Inter", 14d, 400, 500, 10, 1), (d.Theme, d.FontFamily, d.FontSize, d.BodyWeight, d.EmphasisWeight, d.FadeSeconds, d.LimitsRefreshMinutes));
        Assert.Null(d.Extra);
    }

    [Fact]
    public void Only_changed_settings_are_written()
    {
        SettingsStore.Save(SettingsStore.Defaults(), File);
        Assert.Equal("""{"version":1}""", Saved().ToJsonString());

        var st = SettingsStore.Defaults();
        st.FontSize = 16;
        st.Dark.Accent = "#FF8800";
        SettingsStore.Save(st, File);
        Assert.Equal("""{"version":1,"fontSize":16,"dark":{"accent":"#FF8800"}}""", Saved().ToJsonString());
    }

    [Fact]
    public void A_setting_not_in_the_file_follows_the_default()
    {
        System.IO.File.WriteAllText(File, """{ "version": 1, "theme": "Dark" }""");
        var s = SettingsStore.Load(File);
        Assert.Equal(("Dark", 14d, "JetBrains Mono"), (s.Theme, s.FontSize, s.LogFontFamily));
    }

    [Fact]
    public void An_old_full_file_loads_moves_window_values_to_state_and_shrinks_on_the_next_save()
    {
        System.IO.File.WriteAllText(File, """
            { "hiddenApps": ["warp"], "theme": "System", "fontFamily": "Inter", "fontSize": 14, "fadeSeconds": 10,
              "checkUpdates": true, "updateCheckedUtc": "2026-11-15T09:00:00Z", "updateDismissed": "0.3.0", "panelHeight": 420, "light": {}, "dark": {} }
            """);

        var s = SettingsStore.Load(File);
        Assert.True(s.CheckUpdates);
        var state = StateStore.Load(Path.Combine(_dir, StateStore.FileName));
        Assert.Equal((420d, "0.3.0"), (state.PanelHeight, state.UpdateDismissed));
        Assert.Equal(new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc), state.UpdateCheckedUtc!.Value.ToUniversalTime());

        SettingsStore.Save(s, File);
        Assert.Equal("""{"version":1,"hiddenApps":["warp"],"checkUpdates":true}""", Saved().ToJsonString());
    }

    [Fact]
    public void Keys_this_version_does_not_know_are_kept()
    {
        System.IO.File.WriteAllText(File, """{ "version": 1, "futureThing": { "a": 1 } }""");
        var s = SettingsStore.Load(File);
        s.FontSize = 15;
        SettingsStore.Save(s, File);
        Assert.Equal("""{"a":1}""", Saved()["futureThing"]!.ToJsonString());
    }

    [Fact]
    public void A_file_from_a_newer_version_is_read_but_never_written()
    {
        var text = """{ "version": 99, "fontSize": 16 }""";
        System.IO.File.WriteAllText(File, text);
        var s = SettingsStore.Load(File);
        Assert.Equal(16, s.FontSize);
        s.FontSize = 12;
        SettingsStore.Save(s, File);
        Assert.Equal(text, System.IO.File.ReadAllText(File));
    }

    [Fact]
    public void A_broken_file_gives_the_defaults_and_is_kept_in_backups()
    {
        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Equal(14, SettingsStore.Load(File).FontSize);
        Assert.Equal("{ not json", System.IO.File.ReadAllText(Path.Combine(_dir, "backups", "settings.broken.json")));
        Assert.Equal(14, SettingsStore.Load(Path.Combine(_dir, "none.json")).FontSize);
    }

    [Fact]
    public void State_is_remembered_apart_from_settings()
    {
        var file = Path.Combine(_dir, StateStore.FileName);
        Assert.Equal(340, StateStore.Load(file).PanelHeight);
        StateStore.Save(new UiState { PanelHeight = 500, UpdateDismissed = "1.0.0" }, file);
        var s = StateStore.Load(file);
        Assert.Equal((500d, "1.0.0"), (s.PanelHeight, s.UpdateDismissed));
    }
}
