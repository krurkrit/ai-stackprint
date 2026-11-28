// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Models;

/// <summary>
/// User preferences. The defaults and what each one means are in default/settings.json (built in), not here: get a default set from
/// <see cref="Services.SettingsStore.Defaults"/>, not with <c>new</c>. Keys this version does not know are kept (<see cref="Extra"/>).
/// </summary>
public class Settings
{
    [System.Text.Json.Serialization.JsonConstructor]
    internal Settings() { }

    public List<string> HiddenApps { get; set; } = [];
    public bool HideNotInstalled { get; set; }
    public string Theme { get; set; } = "";
    /// <summary>Font settings by key (<see cref="Services.FontRoles"/>): family, size and weight of each, and how they link to each other. Work with them through <see cref="Services.FontRuleSet"/>.</summary>
    public Dictionary<string, FontRule> Fonts { get; set; } = [];
    public bool LogLineNumbers { get; set; }
    public int FadeSeconds { get; set; }
    public bool Animations { get; set; }
    public int VirtualListRows { get; set; }
    public int LimitsRefreshMinutes { get; set; }
    public List<string> LimitsEnabled { get; set; } = [];
    public bool TrayEnabled { get; set; }
    public bool CloseToTray { get; set; }
    public bool StartAtLogin { get; set; }
    public bool WidgetEnabled { get; set; }
    public bool WidgetAtLogin { get; set; }
    public bool WidgetOnTop { get; set; }
    public int WidgetOpacity { get; set; }
    public Dictionary<string, List<string>> WidgetShow { get; set; } = [];
    public bool AlertsEnabled { get; set; }
    public List<int> AlertShortAt { get; set; } = [];
    public int AlertEarlyHours { get; set; }
    public bool AlertReset { get; set; }
    public int AlertLeftHours { get; set; }
    public int AlertLeftPercent { get; set; }
    public bool CheckUpdates { get; set; }
    public bool BenchmarksEnabled { get; set; }
    public ThemeColors Light { get; set; } = new();
    public ThemeColors Dark { get; set; } = new();

    /// <summary>Keys of a newer version (or ones this version dropped): read and written back unchanged.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>Color overrides for one theme as hex (#RRGGBB or #AARRGGBB); null = the app default.</summary>
public class ThemeColors
{
    public string? Accent { get; set; }
    public string? Hover { get; set; }
    public string? Selection { get; set; }
    /// <summary>Dashboard level colors in the order System, Local, Project, User; null or empty entries use the defaults.</summary>
    public string?[]? Levels { get; set; }
}
