# Adding an app

Three ways, easiest first.

1. UI: sidebar > "+ Add app". Pick a name, category and config folder. Saved to `~/.ai-stackprint/apps.json`.
2. Edit `~/.ai-stackprint/apps.json`: `{ "version": 2, "apps": [ … ] }`, each entry with the schema below. Custom apps only: ids start with `custom.` (added if missing), and an app with a built-in id or name is refused. To change a built-in app, contribute the change instead.
3. Contribute a built-in: edit `default/apps.json` (same entries under `"apps"`). When a tool you added yourself becomes built in, the built-in app is used and the Apps page offers to remove your entry.

## Schema
```json
{
  "id": "my-orchestrator",
  "name": "My Orchestrator",
  "category": "orchestrator",
  "logo": "claudecode",
  "detect": ["~/.myapp", "%MYAPP_HOME%"],
  "roots": [{ "path": "~/.myapp/skills", "level": "User", "depth": 4 }],
  "projectRoots": [{ "path": ".myapp/skills", "level": "Project", "layout": "flat-md" }],
  "configs": [
    { "path": "~/.myapp/RULES.md", "level": "User", "kind": "instructions" },
    { "path": "RULES.md", "level": "Project", "kind": "instructions" },
    { "path": "~/.myapp/settings.json", "level": "User", "kind": "settings" }
  ],
  "pluginFormat": "claude",
  "pluginManifest": "~/.myapp/plugins.json",
  "pluginCommands": { "install": ["myapp", "plugin", "install", "{plugin}", "--scope", "{scope}"], "remove": ["myapp", "plugin", "uninstall", "{plugin}", "--scope", "{scope}"] },
  "logs": [
    { "path": "~/.myapp/logs", "pattern": "*.log", "depth": 1, "label": "Logs" },
    { "path": ".myapp/logs", "level": "Project" }
  ],
  "notes": "Shown in the app detail."
}
```
- Paths accept `~` and `%ENV%`.
- `os`: `windows`, `macos` or `linux` limits a root to one OS.
- `level`: `System` (machine-wide, admin-managed), `User`, `Project` (shared in the repo) or `Local` (personal, per project, not committed; e.g. `CLAUDE.local.md`). Project and Local paths are relative to each project folder.
- `label` (roots and configs): the app's own word for the level, e.g. `ADMIN`, `REPO`, `Workspace`, `Managed`. Shown on hover.
- `nested: true` (project roots and project configs): also look in sub-folders, up to 3 levels, skipping hidden and build folders (monorepos).
- `bundled: true` (roots): skills that ship with the app itself; shown as "Bundled" and never removable.
- `layout`: `skill-folders` (default, folders containing `SKILL.md`) or `flat-md` (`*.md` files).
- An app is "installed" when any `detect` folder exists.
- `category`: `agent`, `orchestrator`, `editor`, `chat`, `runtime`, `shared` or `custom`.
- `logo`: optional key from `src/App/Assets/logos.json`. Without it the category icon is shown.
- `configs`: instruction, settings or MCP files (or rules folders) the app reads. `kind`: `instructions`, `settings`, `mcp`, `ignore` (an ignore file such as `.cursorignore`) or `hooks` (a hook file, or a folder of them such as `.github/hooks`; listed on the Hooks page). Project paths are relative to each project folder. Shown on the app page with a read-only preview.
- `parts`: folders of command or agent files (`.claude/commands`, `.github/prompts`, `.gemini/commands`). `kind`: `command` or `agent`; `pattern`: file name patterns separated by `;` (default `*.md`); `depth`: sub-folders read below the path (their names prefix the command: `team:review`); `level`, `os`, `label` as above. The description comes from the front matter, or `description = "…"` in a `.toml` file; an agent's `name` overrides the file name. Project paths are relative to each project folder.
- `pluginCommands`: the app's own CLI for installing/removing plugins. Each item is one argument; `{plugin}` and `{scope}` (`user`, `project`, `local`) are filled in. Project scopes run in the project folder. Without it, plugins are listed but not managed.
- Skills are added to the first non-bundled `skill-folders` root at User level, or the first project root for a project.
- Skill invocation is read from `SKILL.md` front matter: `disable-model-invocation: true` = user only, `user-invocable: false` = agent only, otherwise agent + user.

- `logs[].format`: `claude-session` or `codex-session` enables the Conversation view and the Usage card (conversation with tool calls and sub-agents). New formats: an `ILogParser` registered in `LogParsers.All`.
- `usage`: sources read only for the Usage card, not listed on the Logs page; same fields as `logs`. `opencode-db` reads OpenCode's SQLite database (`~/.local/share/opencode/opencode.db`).
- `logs`: a log file, or a folder searched for `pattern` (default `*.log`) up to `depth` sub-folders down. `label` names the kind (Logs, Debug, Sessions). `os` limits it to one OS. Project-level paths are relative to each project folder. `account` (optional) names whose logs a source holds; with two or more accounts the Usage card gets an account filter (sources without it count as "Default"). For several accounts of one app, add one log source per account folder (for example each Claude Code config folder's `projects`).

## Hide or remove
- Hide any app in Settings (or "Hide" on its page). Hidden apps are not scanned. Stored in `settings.json`.
- Apps added through the UI or `apps.json` can be removed on their page. Before version 2, `apps.json` was a bare list and could change a built-in app; such an entry is now unused (the built-in wins) and listed under "Now built in" on the Apps page, with Remove.

## Orchestrators
Orchestrators such as T3 Code and Orca mostly drive other agents, so their own skills usually live in the agents' folders. Their entries detect the app and scan only what is documented. Verify paths on your machine and extend `roots` in `apps.json`.
