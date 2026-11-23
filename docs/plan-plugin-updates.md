# Plan: plugin update checks and updates (later)

Not started. Nothing here is decided yet: the interface is to be designed first (mockups in `docs/mockups/`, then `decisions.md`), then each app is implemented on its own. Delete this file when the last phase is merged.

Each phase is one Conventional commit (or PR), ends with `dotnet format`, a 0-warning build, all tests passing, and the docs updated.

## Goal
Show which installed plugins have a newer version, and update them from the Plugins page.

## Rules that apply (from `AGENTS.md`)
- Support an app by data in `default/apps.json` (a registry type and an `update` command), not by per-app branches in `Scanner`.
- Network only for a registry the user turned on (Settings, with a consent dialog), one host per switch, at most once a day or on "Check now", nothing about the user sent. Same pattern as live limits and the update check. A check that needs no network (a local marketplace copy) needs no consent.
- Changes go through `PluginManager`: the app's own CLI, no shell, whitelisted arguments, only after the user confirms in a dialog.
- The scanner stays read-only.

## What each group of apps can do
| Group | Apps | Check | Update |
|---|---|---|---|
| Claude Code | Claude Code | Offline: installed version against the local marketplace copy (`~/.claude/plugins/marketplaces/`); a refresh needs the network | Its own CLI (`pluginCommands.update`) |
| VS Code-style extensions (`vscode`) | Cursor, Windsurf, Kiro, Antigravity, Trae, Qoder | Version in the extension manifest against a registry (Open VSX or the VS Code marketplace; differs per app) | The app's CLI where it has one (to verify per app) |
| npm packages (`opencode-config`) | OpenCode | npm registry (latest version of a named package) | Edit the config or run npm: to decide |
| Plain folders (`folders`) | Gemini CLI, GitHub Copilot, Qwen Code, Hermes Agent, OpenClaw, Zed, Claude Desktop | Only where a folder records its source (git remote, manifest URL) | No app command: show version and source only |
| Enabled lists and own formats | Factory Droid, Codex CLI, Goose, OpenCode files | No common source | No |

Plugins with no version (tracked by git commit only) and plugins installed from a local folder cannot be checked.

## To verify before building
- Claude Code: that `claude plugin update` exists in the installed versions, and the marketplace layout and refresh command.
- Each VS Code-style app: its registry, and whether its CLI can install or update an extension by id.
- OpenCode: how a plugin version is pinned in its config.

## Phases (order to be confirmed)
1. **Interface.** Mockups first: an "Update available" mark on the Plugin row (version shown as `1.2.0 → 1.3.0`), an Updates filter menu in the tool bar, an Update button on the row and in the detail panel, the confirm dialog, a "Check for updates" button with its last-checked time, and the consent dialog and Settings switch for each registry. Decide it in `decisions.md`.
2. **Core model.** `Item` gets the latest known version; a service compares versions (semver where possible, otherwise "different"); a cache in `cache/` with the last good copy kept on any failure.
3. **Claude Code.** The offline check and the update command in `apps.json`; Update through `PluginManager`.
4. **VS Code-style extensions.** One registry reader per registry, one phase per app after the first, each verified on a real install.
5. **OpenCode (npm).**
6. **The rest:** show installed version and source only.

## Tests
- Version comparison, the cache, the check for each registry with the network replaced by a test seam (`docs/testing.md`), the update command built with whitelisted arguments, and the UI (mark, filter, confirm dialog) headless.
