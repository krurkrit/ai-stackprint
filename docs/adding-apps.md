# Adding an app

Three ways, easiest first.

1. UI: sidebar > "+ Add app". The dialog is laid out like an entry of `apps.json`: name, category, the config folder (`detect`), then one section per list (`skills`, `configs`, `commands`, `agents`, `logs`) with a row per path: the path, its level and, for config files, its type. Choosing the config folder fills in rows for what it holds (skills/, commands/, agents/, AGENTS.md, settings, MCP files, hooks, logs/); change, remove or add rows (a Project row such as `.myapp/skills` is looked for in every project). Saved to `~/.ai-stackprint/apps.json`.
2. Edit `~/.ai-stackprint/apps.json`: `{ "version": 1, "apps": [ … ] }`, each entry with the schema below. Custom apps only: ids start with `custom.` (added if missing), and an app with a built-in id or name is refused. To change a built-in app, contribute the change instead.
3. Contribute a built-in: edit `default/apps.json` (same entries under `"apps"`). When a tool you added yourself becomes built in, the built-in app is used and the Apps page offers to remove your entry.

## Schema
```json
{
  "id": "my-agent",
  "name": "My Agent",
  "category": "agent",
  "logo": "claudecode",
  "detect": ["~/.myapp", "%MYAPP_HOME%"],
  "skills": [
    { "path": "~/.myapp/skills", "level": "User" },
    { "path": ".myapp/skills", "level": "Project", "nested": true }
  ],
  "configs": [
    { "path": "~/.myapp/RULES.md", "level": "User", "kind": "instructions" },
    { "path": "RULES.md", "level": "Project", "kind": "instructions" },
    { "path": "~/.myapp/settings.json", "level": "User", "kind": "settings" },
    { "path": ".myapp/mcp.json", "level": "Project", "kind": "mcp" }
  ],
  "commands": [{ "path": ".myapp/commands", "level": "Project" }],
  "agents": [{ "path": "~/.myapp/agents" }],
  "pluginFormat": "claude",
  "pluginManifest": "~/.myapp/plugins.json",
  "pluginCommands": { "install": ["myapp", "plugin", "install", "{plugin}", "--scope", "{scope}"], "remove": ["myapp", "plugin", "uninstall", "{plugin}", "--scope", "{scope}"] },
  "plugins": [{ "format": "folders", "path": "~/.myapp/extensions", "manifests": ["plugin.json"], "label": "Extensions" }],
  "logs": [
    { "path": "~/.myapp/logs", "pattern": "*.log", "depth": 1, "label": "Logs", "format": "text-log" },
    { "path": ".myapp/logs", "level": "Project" }
  ],
  "notes": "Shown in the app detail."
}
```

### App
| Field | Meaning |
|---|---|
| `id` | Unique key. Custom apps start with `custom.` (added if missing). |
| `name` | Shown everywhere. |
| `category` | `agent`, `orchestrator`, `editor`, `chat`, `runtime`, `shared` or `custom`. |
| `logo` | Optional key from `src/App/Assets/logos.json`. Without it the category icon is shown. |
| `detect` | Folders; the app is "installed" when any of them exists. |
| `skills` | Skills folders (below). |
| `configs` | Instruction, settings, MCP, ignore or hook files, or rules folders (below). Shown on the app page with a read-only preview. |
| `commands` | Folders of command files (saved prompts you run, such as `/review`): the Commands page. |
| `agents` | Folders of agent files (sub-agents the main agent hands work to): the Agents page. |
| `pluginFormat` + `pluginManifest` | The app's own list of installed plugins and the `IPluginReader` that reads it. |
| `pluginCommands` | The app's own CLI for installing and removing plugins. Each item is one argument; `{plugin}` and `{scope}` (`user`, `project`, `local`) are filled in. Project scopes run in the project folder. Without it, plugins are listed but not managed. |
| `plugins` | More plugin or extension folders, read the same way for every app (below). |
| `logs` | Log files or folders for the Logs page (below). |
| `usage` | Sources read only for the Usage card, not listed on the Logs page; same fields as `logs`. `opencode-db` reads OpenCode's SQLite database. |
| `context` | Id of an `IContextRule`: what the app loads into context in a project (the project page's context budget). |
| `limits` | Id of an `ILimitProvider`: live plan and usage limits, off until the user turns them on. |
| `notes` | Shown on the app page. Say here when a path is not documented by the vendor ("best-effort"). |

### Fields every list item may have
- `path`: a folder or file. Paths accept `~` and `%ENV%`. Project and Local paths are relative to each project folder.
- `level`: `System` (machine-wide, admin-managed), `User` (default), `Project` (shared in the repo) or `Local` (personal, per project, not committed; e.g. `CLAUDE.local.md`).
- `os`: `windows`, `macos` or `linux` limits the item to one OS; left out, it applies to all.
- `label`: the app's own word for the level or kind, e.g. `ADMIN`, `REPO`, `Workspace`, `Managed`, `Prompts`. Shown on hover.

### `skills`
- `layout`: `skill-folders` (default, folders containing `SKILL.md`) or `flat-md` (`*.md` files).
- `depth`: sub-folder levels searched (default 4).
- `nested: true` (Project): also look in sub-folders of the project, up to 3 levels, skipping hidden and build folders (monorepos).
- `bundled: true`: skills that ship with the app itself; shown as "Bundled" and never removable.
- Skills are added to the first non-bundled `skill-folders` folder at User level, or the first Project one for a project.
- Who can start a skill is read from `SKILL.md` front matter: `disable-model-invocation: true` = user only, `user-invocable: false` = agent only, otherwise agent and user.

### `configs`
- `kind`: `instructions` (default), `settings`, `mcp`, `ignore` (an ignore file such as `.cursorignore`) or `hooks` (a hook file, or a folder of them such as `.github/hooks`; listed on the Hooks page).
- `nested: true` (Project): also look in sub-folders.
- `appWritten: true`: the app rewrites the file on its own (state, counters), so its edits are not listed as changes.

### `commands` and `agents`
- `pattern`: file name patterns separated by `;` (default `*.md`), e.g. `*.prompt.md`, `*.toml`.
- `depth`: sub-folders read below the path (default 2); their names prefix the name: `team/review.md` is `team:review`.
- The description comes from the front matter, or `description = "…"` in a `.toml` file; an agent's `name` overrides the file name.

### `plugins`
- `format`: the reader: `folders` (default; folders with a manifest), `vscode`, `enabled-list`, `opencode-config`, `opencode-files`, `goose`, `codex`.
- `manifests` (`folders`): manifest files to look for, first found wins (default: the known names, such as `plugin.json`).
- `depth`: how deep plugin folders sit under the path (default 2).

### `logs` and `usage`
- A log file, or a folder searched for `pattern` (default `*.log`) up to `depth` sub-folders down (default 0).
- `label` names the kind (Logs, Debug, Sessions, Chat history).
- `format`: `claude-session` or `codex-session` enables the Conversation view and the Usage card (conversation with tool calls and sub-agents); `text-log` is a plain text log. New formats: an `ILogParser` registered in `LogParsers.All`.
- `account` (optional) names whose logs a source holds; with two or more accounts the Usage card gets an account filter (sources without it count as "Default"). For several accounts of one app, add one log source per account folder (for example each Claude Code config folder's `projects`).

### Older field names
Apps saved by earlier 0.x versions used `roots` and `projectRoots` (now `skills`; a `projectRoots` folder is read at Project level unless it says Local) and `parts` with a `kind` (now `commands` or `agents`). They are still read and saved with the new names the next time the file is written.

## Hide or remove
- Hide any app in Settings (or "Hide" on its page). Hidden apps are not scanned. Stored in `settings.json`.
- Apps added through the UI or `apps.json` can be removed on their page. An older `apps.json` could hold a bare list that changed a built-in app; such an entry is now unused (the built-in wins) and listed under "Now built in" on the Apps page, with Remove.

## Orchestrators
Orchestrators such as T3 Code and Orca mostly drive other agents, so their own skills usually live in the agents' folders. Their entries detect the app and scan only what is documented. Verify paths on your machine and extend `skills` in `apps.json`.
