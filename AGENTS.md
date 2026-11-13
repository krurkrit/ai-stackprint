# AGENTS.md

Source of truth for AI coding agents (Codex, Claude Code, others). `CLAUDE.md` imports this file; edit here only.

## Project
AI Stackprint: cross-platform desktop app (Avalonia 12, .NET 10, C# only, no XAML) that inventories AI apps, orchestrators, plugins, skills, config files and logs on this machine, by level (Project / Local / User / System).

## Commands
- Run: `dotnet run`
- Build: `dotnet build`
- Publish: `dotnet publish -c Release -r <win-x64|linux-x64|osx-arm64> --self-contained false`
- Test: `dotnet test tests/AiStackprint.Tests` (xunit). If you add logic to `src/Services`, add tests for it.

## Layout
- `Assets/apps.builtin.json`: built-in app definitions (data, embedded resource)
- `Assets/icons.json`, `Assets/logos.json`: UI icons and app logos as path data (embedded); see `docs/architecture.md`
- `src/Models`: records and definition classes
- `src/Services`: `AppCatalog` (built-in + user `apps.json`), `SettingsStore` (`settings.json`), `Scanner` (generic, read-only), `PluginReaders` (per-format), `SkillManager` (add/remove skills), `PluginManager` (runs the app's plugin CLI), `LogReader` (finds and tails log files), `LogParsers` (session logs → readable steps), `LogSearch`, `LogSummary`, `LogFolderWatcher` (watches an app's log folders), `LogTrash` (deletes logs to Recycle Bin/Trash), `ConfigEditor` (validated, backed-up saves of config files), `Contrast` (WCAG ratios), `SkillDoc` (front matter, links), `SkillCheck` + `Health` (checks), `ContextBudget` + `ContextRules` (what each app loads into context per project), `LimitsService` + `LimitProviders` (live plan and limits), `ScanDiff` (changes since the last scan), `CrashLog` (crash.log)
- `src/Ui`: `MainWindow` (nav + routing; `.Nav`: history, shortcuts, limits timer; `.TitleBar`: custom title bar: name, back/forward; F11 full screen), `CommandPalette` (Ctrl+K), `DashboardView`, `AppsView`, `AppDetailView` (+ `UsageCard`, `LimitsView`), `AppActions`, `LibraryView` (plugins/skills page) + `ItemBrowser` (list, filters, bottom panel) + `DetailPanel` + `FileView`, `ProjectView` (+ `ContextSection`), `HealthView`, `LogsView`, `LogViewer`, `LogSummaryCard`, `ReadableLog`, `Levels` (order + explanations), `SettingsView`, `AppearanceSection`, `ColorSettings`, `CodeView` (the only AvaloniaEdit user), `ConfigPanel`, `Dialogs`, `AddAppDialog`, `Table`, `W` (widget helpers), `Busy` (the one loading indicator), `MemoryTrim` (debounced GC after a page or the last code view closes), `Toast`, `Theme` (colors + fonts), `Icons`, `Ctx`
- `tests/AiStackprint.Tests`: xunit tests for `src/Services` (excluded from the app build)
- `docs/`: architecture and how-to guides
- `docs/decisions.md`: agreed product and UI decisions; read before UI work

## Rules
- Support a new app by adding data to `Assets/apps.builtin.json`. Do not add per-app `if` branches in `Scanner`.
- Support a new plugin manifest format by adding an `IPluginReader` and registering it in `PluginReaders.All`. Same pattern for context rules (`IContextRule` in `ContextBudget.Rules`, app's `context` field) and live limits (`ILimitProvider` in `LimitProviders.All`, app's `limits` field).
- Live limits: network calls only for apps the user turned on (Settings › Usage limits, with a consent dialog); at most one call per app per refresh interval (one minute minimum); each provider calls only its own host. Tokens stay in memory: never shown, logged, saved or refreshed; credential files are opened read-only.
- Every API call or long operation shows `Busy` (spinner after 300 ms); a refresh keeps the last data on screen with a small spinner instead of blanking it. Errors replace the spinner with a message and Retry.
- Colors: Snowy Owl tokens (`snowy-owl/docs/TOKENS.md`) are defined once in `Theme`; users may override accent, hover and selection per theme (Settings › Appearance). Views use `W` resource keys through `W.Themed` (light/dark automatic). No hex anywhere else.
- Font and sizes: Appearance settings (default Inter → Noto Sans → platform default, 14 px, weights 400/500). Write sizes for a 14 px base through `W.Txt`/`W.Title`/`W.Size` so they scale; use `W.Body`/`W.Emphasis`/`W.Heading`, never fixed weights. Fonts are not bundled or downloaded.
- Levels are always listed highest priority first: System, Local, Project, User (`Levels.Order`).
- The scanner is read-only. Only `SkillManager`, `PluginManager`, `ConfigEditor` and `LogTrash` change anything, and only after the user confirms in a dialog. Removed skills are moved to the backup folder, never deleted. Saved config files are copied to the backups folder first and refused if they changed on disk. Plugin changes go through the app's own CLI, run without a shell, with whitelisted arguments. Logs are viewed read-only; deleting moves only files the app listed as logs to the Recycle Bin/Trash.
- Third-party UI code: AvaloniaEdit + TextMate (MIT) only inside `CodeView`.
- Catch only expected IO/JSON exceptions; never swallow broadly. Fatal errors are written to crash.log (`CrashLog`), then the app closes as before.
- A control kept in a field and shown again on a rebuilt page must be taken from its old parent first (`.Free()`); otherwise Avalonia throws "already has a parent" and the app closes.
- Items are records whose lists compare by reference: to find "the same" skill or plugin after a rescan use `ItemBrowser.Same`, not `==` or `Contains`.
- Keep one class per concern; keep files under about 250 lines.
- Verify with `dotnet build` before finishing. Report what could not be verified.

## Do not guess
Skill/config paths for apps without documented locations are best-effort. Mark them with a `notes` field in the JSON instead of presenting them as fact.
