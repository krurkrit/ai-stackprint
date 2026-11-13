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
    public IReadOnlyList<string> Invokers { get; init; } = Array.Empty<string>();   // skills: who can start it, "Agent" and/or "User" (from SKILL.md front matter)
    public string? Version { get; init; }         // plugins: version from the app's plugin registry
    public string? Author { get; init; }          // plugin manifest author, or SKILL.md author / metadata.author
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
    public string Kind { get; set; } = "instructions"; // instructions | settings | mcp
    public string? Os { get; set; }
    public string? Label { get; set; }
    public bool Nested { get; set; }                   // Project files: also look in sub-folders
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
    public List<string> Install { get; set; } = new();
    public List<string> Remove { get; set; } = new();
}

public enum AppOrigin { BuiltIn, Custom, Override }

/// <summary>Data-driven description of an AI app. Built-ins live in Assets/apps.builtin.json.</summary>
public class AppDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "agent";  // agent | orchestrator | editor | chat | runtime | shared | custom
    public string? Logo { get; set; }                         // key in Assets/logos.json; null = category icon
    public List<string> Detect { get; set; } = new();        // any existing folder => installed
    public List<ScanRoot> Roots { get; set; } = new();        // user/system-level locations
    public List<ScanRoot> ProjectRoots { get; set; } = new(); // paths relative to a project folder
    public List<ConfigFile> Configs { get; set; } = new();
    public string? PluginFormat { get; set; }                 // key of an IPluginReader
    public string? PluginManifest { get; set; }
    public PluginCommands? PluginCommands { get; set; }
    public List<LogSource> Logs { get; set; } = new();
    public List<LogSource> Usage { get; set; } = new();       // usage-only sources (an app's database): Usage card, not the Logs page
    public string? Context { get; set; }                       // IContextRule id: what the app loads into context in a project (claude, codex, gemini, copilot, cursor)
    public string? Limits { get; set; }                        // ILimitProvider id: live plan and usage limits from the app's saved sign-in
    public string? Notes { get; set; }
    [JsonIgnore] public AppOrigin Origin { get; set; }
}

/// <summary>User preferences, saved to settings.json next to apps.json.</summary>
public class Settings
{
    public List<string> HiddenApps { get; set; } = new();
    public bool HideNotInstalled { get; set; }
    public string Theme { get; set; } = "System";      // System | Light | Dark
    public string FontFamily { get; set; } = "Inter";
    public double FontSize { get; set; } = 14;          // base size, 12-18
    public int BodyWeight { get; set; } = 400;          // 300-700 (same list as EmphasisWeight)
    public int EmphasisWeight { get; set; } = 500;      // 300-700
    public string LogFontFamily { get; set; } = "JetBrains Mono";   // logs and the config editor
    public double LogFontSize { get; set; } = 14;          // 10-18
    public string HeadingFontFamily { get; set; } = "";   // empty = same as Body
    public int HeadingWeight { get; set; }              // 0 = derived from Emphasis; else 300-700
    public int LogFontWeight { get; set; } = 400;       // Code & logs
    public int MenuWeight { get; set; } = 500;          // side menu items
    public bool LogLineNumbers { get; set; }            // logs only; the config editor always shows them
    public int FadeSeconds { get; set; } = 10;         // new log lines/steps stay highlighted and fade out over this many seconds; 0 = off
    public int LimitsRefreshMinutes { get; set; } = 1; // live usage limits: at most one call per app per this many minutes (min 1)
    public List<string> LimitsEnabled { get; set; } = new();   // app ids whose saved sign-in may be used to ask the provider for limits (opt-in)
    public double PanelHeight { get; set; } = 340;      // bottom detail panel on the Skills, Plugins and project pages
    public ThemeColors Light { get; set; } = new();
    public ThemeColors Dark { get; set; } = new();
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
    public List<AppDefinition> Defs { get; set; } = new();
    public Settings Settings { get; set; } = new();
    public List<string> Projects { get; } = new();
    public List<Item> Items { get; set; } = new();
    public List<AppInfo> Apps { get; set; } = new();
    public Services.ScanChanges Changes { get; set; } = new(new(), null);   // since the scan before this one (saved between runs)
    public List<Services.HealthIssue> Health { get; set; } = new();                 // checks of the last scan

    public AppDefinition? Def(string id) => Defs.FirstOrDefault(d => d.Id == id);
}
