// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace AiStackprint.Models;

/// <summary>One row in the inventory: an app, a plugin, a skill or a config file (Kind "Config").</summary>
public record Item(string Kind, string Name, string Description, string Source, string Level, string Path)
{
    public string App { get; init; } = "";        // AppDefinition.Id that produced the row
    public string? Project { get; init; }         // project folder, for Project-level rows
    public string? Plugin { get; init; }          // plugin id when a skill ships inside a plugin
    public string? Scope { get; init; }           // raw plugin scope (user | project | local | managed)
    public bool Bundled { get; init; }            // ships with the app itself (e.g. Codex system skills)
    public string? LevelLabel { get; init; }      // the app's own word for the level (REPO, ADMIN, Workspace)
    public IReadOnlyList<string> Invokers { get; init; } = [];   // skills: who can start it, "Agent" and/or "User" (from SKILL.md front matter)
    public string? Version { get; init; }         // plugins: version from the app's plugin registry
    public string? Author { get; init; }          // plugin manifest author, or SKILL.md author / metadata.author
    public IReadOnlyList<string> Keywords { get; init; } = [];   // plugins: keywords from the manifest (search)
    public IReadOnlyList<string> Provides { get; init; } = [];   // plugins: what it adds, PluginParts keys (skills, commands, agents, hooks, mcp, lsp)
    public bool AppWritten { get; init; }        // config files the app rewrites on its own: edits are not listed as changes
}

public record AppInfo(string Id, string Name, string Category, string? Logo, bool Installed, string? Folder);

public record ScanResult(List<Item> Items, List<AppInfo> Apps);

/// <summary>A folder to scan. Layout: "skill-folders" (…/name/SKILL.md) or "flat-md" (*.md files).</summary>
public class ScanRoot
{
    public string Path { get; set; } = "";
    public string Level { get; set; } = "User";      // Project | Local | User | System
    public string Layout { get; set; } = "skill-folders";
    public int Depth { get; set; } = 4;
    public string? Os { get; set; }                  // windows | macos | linux; null = all
    public string? Label { get; set; }               // the app's own word for this level
    public bool Nested { get; set; }                 // Project roots: also look in sub-folders (monorepos)
    public bool Bundled { get; set; }                // skills here ship with the app; read-only
}

/// <summary>An instructions, settings or MCP file (or folder) the app reads. Project paths are relative to a project folder.</summary>
public class ConfigFile
{
    public string Path { get; set; } = "";
    public string Level { get; set; } = "User";
    public string Kind { get; set; } = "instructions"; // instructions | settings | mcp | ignore
    public string? Os { get; set; }
    public string? Label { get; set; }
    public bool Nested { get; set; }                   // Project files: also look in sub-folders
    public bool AppWritten { get; set; }               // the app rewrites it on its own (state, counters): edits are not listed as changes
}

/// <summary>A folder of command or agent files (one file each: .claude/commands, .github/prompts, .gemini/commands …). Project paths are relative to a project folder.</summary>
public class PartSource
{
    public string Path { get; set; } = "";
    public string Level { get; set; } = "User";
    public string Kind { get; set; } = "command";       // command | agent
    public string Pattern { get; set; } = "*.md";       // file name patterns, separated by ;
    public int Depth { get; set; } = 2;                 // sub-folders read below Path (their names prefix the command: team:review)
    public string? Os { get; set; }
    public string? Label { get; set; }                  // the app's own word (Prompts, Workflows, Droids)
}

/// <summary>Where an app writes logs: a file, or a folder searched for <see cref="Pattern"/> up to <see cref="Depth"/> levels down.</summary>
public class LogSource
{
    public string Path { get; set; } = "";
    public string Pattern { get; set; } = "*.log";
    public int Depth { get; set; }
    public string Label { get; set; } = "Logs";        // e.g. Logs, Sessions, MCP
    public string Level { get; set; } = "User";        // Project paths are relative to a project folder
    public string? Os { get; set; }
    public string? Format { get; set; }                // parser for the Conversation view (claude-session, codex-session)
    public string? Account { get; set; }               // names whose logs these are when several accounts use the app (Usage card gets a filter)
}

public record LogFile(string App, string Label, string Path, long Size, DateTime Modified, string? Project, string? Format = null, string? Account = null);

/// <summary>Argument lists for the app's own plugin CLI. Tokens: {plugin}, {scope}. Run without a shell.</summary>
public class PluginCommands
{
    public List<string> Install { get; set; } = [];
    public List<string> Remove { get; set; } = [];
}

/// <summary>
/// Where an app keeps installed plugins or extensions, and how to read them (an <c>IPluginReader</c> format: codex, folders, vscode,
/// opencode-files, opencode-config, goose, enabled-list). A Project-level path is relative to each project folder.
/// </summary>
public class PluginSource
{
    public string Format { get; set; } = "folders";
    public string Path { get; set; } = "";
    public string Level { get; set; } = "User";              // User | System | Project
    public string? Os { get; set; }                          // windows | macos | linux; null = all
    public string? Label { get; set; }                       // the app's own word: Extension, Plugin
    public List<string> Manifests { get; set; } = [];        // folders: manifest files to look for, first found wins (default: PluginManifests.Known)
    public int Depth { get; set; } = 2;                      // folders: how deep plugin folders sit under Path
}

public enum AppOrigin { BuiltIn, Custom }

/// <summary>Data-driven description of an AI app. Built-ins live in default/apps.json; custom apps in the user's apps.json.</summary>
public class AppDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "agent";  // agent | orchestrator | editor | chat | runtime | shared | custom
    public string? Logo { get; set; }                         // key in Assets/logos.json; null = category icon
    public List<string> Detect { get; set; } = [];        // any existing folder => installed
    public List<ScanRoot> Roots { get; set; } = [];        // user/system-level locations
    public List<ScanRoot> ProjectRoots { get; set; } = []; // paths relative to a project folder
    public List<ConfigFile> Configs { get; set; } = [];
    public List<PartSource> Parts { get; set; } = [];      // command and agent folders
    public string? PluginFormat { get; set; }                 // key of an IPluginReader
    public string? PluginManifest { get; set; }
    public PluginCommands? PluginCommands { get; set; }
    public List<PluginSource> Plugins { get; set; } = [];   // more plugin and extension locations, read the same way for every app
    public List<LogSource> Logs { get; set; } = [];
    public List<LogSource> Usage { get; set; } = [];       // usage-only sources (an app's database): Usage card, not the Logs page
    public string? Context { get; set; }                       // IContextRule id: what the app loads into context in a project (claude, codex, gemini, copilot, cursor)
    public string? Limits { get; set; }                        // ILimitProvider id: live plan and usage limits from the app's saved sign-in
    public string? Notes { get; set; }
    [JsonIgnore] public AppOrigin Origin { get; set; }
}

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
    public int VirtualListRows { get; set; }
    public int LimitsRefreshMinutes { get; set; }
    public List<string> LimitsEnabled { get; set; } = [];
    public bool CheckUpdates { get; set; }
    public bool BenchmarksEnabled { get; set; }
    public ThemeColors Light { get; set; } = new();
    public ThemeColors Dark { get; set; } = new();

    /// <summary>Keys of a newer version (or ones this version dropped): read and written back unchanged.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>What the app remembers on this machine (state.json): not a preference, never exported.</summary>
public class UiState
{
    public double PanelHeight { get; set; } = 340;      // bottom detail panel on the Skills, Plugins and project pages
    public DateTime? UpdateCheckedUtc { get; set; }     // last successful update check
    public string? UpdateDismissed { get; set; }        // "Later" on this version: no notice until a newer one
    public DateTime? ModelDataCheckedUtc { get; set; }  // last model data check, whatever its result
    public string? ModelDataEtag { get; set; }          // GitHub's ETag of the copy in cache/models.json (If-None-Match)
    public string? ModelDataResult { get; set; }        // what the last check found, as shown in Settings
    public string? BenchmarkKey { get; set; }           // your Artificial Analysis API key: machine-only (state.json is never exported), never shown or logged
    public DateTime? BenchmarkCheckedUtc { get; set; }  // last benchmark download, whatever its result
    public string? BenchmarkResult { get; set; }        // what it found, as shown in Settings
    public List<string> ComparedModels { get; set; } = [];   // models in the Compare tab's slots of the Models page, in slot order
    public string ModelsTab { get; set; } = "Catalog";      // the tab the Models page was left on: Catalog or Compare
}

/// <summary>Color overrides for one theme as hex (#RRGGBB or #AARRGGBB); null = Snowy Owl default.</summary>
public class ThemeColors
{
    public string? Accent { get; set; }
    public string? Hover { get; set; }
    public string? Selection { get; set; }
    /// <summary>Dashboard level colors in the order System, Local, Project, User; null or empty entries use the defaults.</summary>
    public string?[]? Levels { get; set; }
}

public class AppState
{
    public List<AppDefinition> Defs { get; set; } = [];
    public Settings Settings { get; set; } = Services.SettingsStore.Defaults();
    public UiState State { get; set; } = new();
    public List<string> Projects { get; } = [];                                    // in the order added; saved in projects.json
    public Dictionary<string, DateTime> ProjectAdded { get; } = [];              // when each was added (UTC), kept when one is relocated
    public List<Item> Items { get; set; } = [];
    public List<AppInfo> Apps { get; set; } = [];
    public Services.ScanChanges Changes { get; set; } = new([], null);   // since the scan before this one (saved between runs)
    public List<Services.HealthIssue> Health { get; set; } = [];                 // checks of the last scan, except the projects' (they follow the scan: see ProjectHealth)
    public List<Services.HealthIssue>? ProjectHealth { get; set; }               // the projects' checks of the last scan; null until they are done (they run after the dashboard shows)
    public bool HealthReady => ProjectHealth != null;
    public IEnumerable<Services.HealthIssue> AllHealth => ProjectHealth == null ? Health : Health.Concat(ProjectHealth);
    public List<Services.McpServer> Mcp { get; set; } = [];                      // MCP and LSP servers in the config files and plugins of the last scan
    public List<Services.Hook> Hooks { get; set; } = [];                         // hooks in the settings and hook files and plugins of the last scan

    public List<AppDefinition> ShadowedApps { get; set; } = [];                  // your apps.json entries for tools that are now built in (not used)

    public AppDefinition? Def(string id) => Defs.FirstOrDefault(d => d.Id == id);

    public void Use(Services.Catalog c) { Defs = c.Defs; ShadowedApps = c.Shadowed; }
}
