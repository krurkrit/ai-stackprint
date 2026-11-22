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
    public void A_value_of_the_wrong_type_or_a_file_that_cannot_be_read_gives_the_defaults()
    {
        System.IO.File.WriteAllText(File, """{ "version": 1, "hideNotInstalled": "yes please" }""");
        Assert.False(SettingsStore.Load(File).HideNotInstalled);
        var folder = Path.Combine(_dir, "a-folder.json");
        Directory.CreateDirectory(folder);
        Assert.Equal(SettingsStore.Defaults().HiddenApps, SettingsStore.Load(folder).HiddenApps);
    }

    [Fact]
    public void The_data_folder_file_is_the_default_and_saving_the_defaults_reads_back_the_same()
    {
        Assert.Equal(Path.Combine(DataFolder.Root, "settings.json"), SettingsStore.File);
        SettingsStore.Save(SettingsStore.Defaults());
        Assert.Null(SettingsStore.Problem);
        Assert.Equal(SettingsStore.Defaults().HideNotInstalled, SettingsStore.Load().HideNotInstalled);
    }

    [Fact]
    public void State_that_cannot_be_read_or_written_is_new_and_kept_for_this_run_only()
    {
        var folder = Path.Combine(_dir, "state-folder.json");
        Directory.CreateDirectory(folder);
        Assert.Null(StateStore.Load(folder).BenchmarkKey);
        System.IO.File.WriteAllText(File, """{ "version": 1, "panelHeight": "tall" }""");
        Assert.Null(StateStore.Load(File).BenchmarkKey);
        StateStore.Save(new UiState(), Path.Combine(File, "state.json"));   // under a file: no error
        StateStore.Adopt(new JsonObject { ["panelHeight"] = 300 }, folder);   // cannot be read: nothing taken over
        Assert.True(Directory.Exists(folder));

        Assert.Equal(Path.Combine(DataFolder.Root, "state.json"), StateStore.File);
        StateStore.Save(StateStore.Load());
        Assert.True(System.IO.File.Exists(StateStore.File));
    }

    [Fact]
    public void Every_setting_has_a_default_in_default_settings_json()
    {
        var defaults = SettingsStore.DefaultJson();
        var names = typeof(Settings).GetProperties().Where(p => p.Name != nameof(Settings.Extra)).Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]);
        Assert.DoesNotContain(names, n => !defaults.ContainsKey(n));
        var d = SettingsStore.Defaults();
        Assert.Equal(("System", "Inter", 14d, 400, 600, 700, 500, 10, 1),
            (d.Theme, d.Font().Family, d.Font().Size, d.Font().Weight, d.Font(FontRoles.Emphasis).Weight, d.Font(FontRoles.Heading).Weight, d.Font(FontRoles.Menu).Weight, d.FadeSeconds, d.LimitsRefreshMinutes));
        Assert.Null(FontRuleSet.Validate(d.Fonts));
        Assert.Null(d.Extra);
    }

    [Fact]
    public void Only_changed_settings_are_written()
    {
        SettingsStore.Save(SettingsStore.Defaults(), File);
        Assert.Equal("""{"version":1}""", Saved().ToJsonString());

        var st = SettingsStore.Defaults();
        st.SetSize(16);
        st.Dark.Accent = "#FF8800";
        SettingsStore.Save(st, File);
        Assert.Equal("""{"version":1,"fonts":{"body":{"size":{"value":16}}},"dark":{"accent":"#FF8800"}}""", Saved().ToJsonString());
    }

    [Fact]
    public void A_setting_not_in_the_file_follows_the_default()
    {
        System.IO.File.WriteAllText(File, """{ "version": 1, "theme": "Dark" }""");
        var s = SettingsStore.Load(File);
        Assert.Equal(("Dark", 14d, "JetBrains Mono"), (s.Theme, s.Font().Size, s.Font(FontRoles.Log).Family));
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
        s.SetSize(15);
        SettingsStore.Save(s, File);
        Assert.Equal("""{"a":1}""", Saved()["futureThing"]!.ToJsonString());
    }

    [Fact]
    public void A_file_from_a_newer_version_is_read_but_never_written()
    {
        var text = """{ "version": 99, "fonts": { "body": { "size": { "value": 16 } } } }""";
        System.IO.File.WriteAllText(File, text);
        var s = SettingsStore.Load(File);
        Assert.Equal(16d, s.Font().Size);
        s.SetSize(12);
        SettingsStore.Save(s, File);
        Assert.Equal(text, System.IO.File.ReadAllText(File));
    }

    [Fact]
    public void A_broken_file_gives_the_defaults_and_is_kept_in_backups()
    {
        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Equal(14d, SettingsStore.Load(File).Font().Size);
        Assert.Equal("{ not json", System.IO.File.ReadAllText(Path.Combine(_dir, "backups", "settings.broken.json")));
        Assert.Equal(14d, SettingsStore.Load(Path.Combine(_dir, "none.json")).Font().Size);
    }

    [Fact]
    public void State_is_remembered_apart_from_settings()
    {
        var file = Path.Combine(_dir, StateStore.FileName);
        Assert.Equal(340, StateStore.Load(file).PanelHeight);
        StateStore.Save(new UiState
        {
            PanelHeight = 500,
            UpdateDismissed = "1.0.0"
        }, file);
        var s = StateStore.Load(file);
        Assert.Equal((500d, "1.0.0"), (s.PanelHeight, s.UpdateDismissed));
    }

    [Fact]
    public void Models_ticked_for_the_comparison_are_saved_in_state_json_in_order()
    {
        var file = Path.Combine(_dir, StateStore.FileName);
        StateStore.Save(new UiState { ComparedModels = ["gpt-6", "claude-opus-5-5"] }, file);
        Assert.Contains("comparedModels", System.IO.File.ReadAllText(file));
        Assert.Equal(["gpt-6", "claude-opus-5-5"], StateStore.Load(file).ComparedModels);
    }
}
