# AGENTS.md

Source of truth for AI coding agents (Codex, Claude Code, others). `CLAUDE.md` imports this file; edit here only.

## Project
AI Stackprint: cross-platform desktop app (Avalonia 12, .NET 10, C# only, no XAML) that inventories AI apps, orchestrators, plugins, skills, config files and logs on this machine, by level (Project / Local / User / System).

## Commands
- Run: `dotnet run --project src/App`
- Build: `dotnet build` (the solution `AiStackprint.slnx`: all projects)
- Publish: `dotnet publish src/App -c Release -r <win-x64|linux-x64|osx-arm64> --self-contained false`
- Installer: `pwsh build/pack.ps1 -Runtime <win-x64|linux-x64|osx-arm64>` (Velopack, self-contained; on that platform). Releases: push tag `v<Version>`; `.github/workflows/release.yml` builds all three and publishes the GitHub release with notes from git-cliff (`cliff.toml`). See `docs/releasing.md`.
- Test: `dotnet test` (all), or `dotnet test tests/AiStackprint.Core.Tests` (services; builds Core only, no UI). If you add logic to `src/Core/Services`, add tests for it. UI tests run headless (Avalonia.Headless.XUnit, xUnit v3): mark them `[AvaloniaFact]`; `TestApp` starts the real app styles without a window or scan; helpers in `Ui` (show, find texts and buttons, click, wait for background work). Pass `TestContext.Current.CancellationToken` where a method takes a token.
- Format: `dotnet format` before committing. Style and analyzers follow dotnet/aspnetcore's `.editorconfig`: braces on every `if`/loop, Allman braces, `_camelCase` private and internal fields (static too), PascalCase constants, `var`, file-scoped namespaces, System usings first, no unused usings, `ConfigureAwait(false)` in Core (not in UI code). Sonar's default C# rules also run on build (`SonarAnalyzer.CSharp`, a build-only package); the few turned off are listed with reasons in `.editorconfig`, and a single justified exception uses `#pragma warning disable Sxxx // reason`. Analyzer and style warnings show on build; keep it at 0 warnings.
- Every `.cs` file starts with the license header (`file_header_template` in `.editorconfig`; `dotnet format` adds it). The license is MIT (`LICENSE`).
- SDK: .NET 10 (LTS), pinned in `global.json`. Package versions are set once in `Directory.Packages.props` (exact versions; projects list packages without versions). Shared build settings: `Directory.Build.props`.

## Layout
- `src/App/AiStackprint.App.csproj` (the app, builds `AiStackprint.exe`: `Ui`, `Program.cs`, `Assets`) references `src/Core/AiStackprint.Core.csproj` (models and services, no Avalonia). Services must not depend on UI code; the build enforces it.
- `src/Core/Assets/apps.builtin.json`: built-in app definitions (data, embedded resource in Core)
- `src/App/Assets/icons.json`, `src/App/Assets/logos.json`: UI icons and app logos as path data (embedded); see `docs/architecture.md`
- `src/Core/Models`: records and definition classes
- `src/Core/Services`: `AppCatalog` (built-in + user `apps.json`), `SettingsStore` (`settings.json`), `Scanner` (generic, read-only; `.Skills`: skill folders), `PluginReaders` (per-format), `SkillManager` (add/remove skills), `PluginManager` (runs the app's plugin CLI), `LogReader` (finds and tails log files), `LogParsers` (session logs → readable steps; one file per parser: `ClaudeSessionParser`, `CodexSessionParser`), `LogSearch`, `LogSummary`, `LogFolderWatcher` (watches an app's log folders), `LogTrash` (deletes logs to Recycle Bin/Trash), `ConfigEditor` (validated, backed-up saves of config files), `Contrast` (WCAG ratios), `SkillDoc` (front matter, links), `SkillCheck` + `Health` (checks), `ContextBudget` + `ContextRules` (what each app loads into context per project), `LimitsService` + `LimitProviders` (live plan and limits), `ScanDiff` (changes since the last scan), `UpdateCheck` (latest GitHub release vs this version), `CrashLog` (crash.log)
- `src/App/Ui`: `MainWindow` (nav + routing; `.Actions`: refresh, rescan, add project/app; `.Nav`: history, shortcuts, limits timer; `.TitleBar`: custom title bar: name, back/forward; F11 full screen), `CommandPalette` (Ctrl+K), `DashboardView`, `AppsView`, `AppDetailView` (+ `UsageCard`, `LimitsView`), `AppActions`, `LibraryView` (plugins/skills page) + `ItemBrowser` (list, filters, bottom panel) + `DetailPanel` + `FileView`, `ProjectView` (+ `ContextSection`), `HealthView`, `LogsView`, `LogViewer`, `LogSummaryCard`, `ReadableLog`, `Levels` (order + explanations), `SettingsView`, `AppearanceSection`, `ColorSettings`, `CodeView` (the only AvaloniaEdit user), `ConfigPanel`, `Dialogs`, `AddAppDialog`, `Table`, `W` (widget helpers), `Busy` (the one loading indicator), `MemoryTrim` (debounced GC after a page or the last code view closes), `UpdateView` (Settings › Updates, new-version notice), `Toast`, `Theme` (colors + fonts), `Icons`, `Ctx`
- `tests/AiStackprint.Core.Tests`: xUnit v3 tests for Core; `tests/AiStackprint.App.Tests`: headless tests for UI code
- `build/pack.ps1`: Velopack installer for one platform; `.github/workflows/release.yml`: release on a version tag
- `docs/`: architecture and how-to guides (`releasing.md`: versions, tags, installers)
- `docs/decisions.md`: agreed product and UI decisions; read before UI work

## Rules
- Support a new app by adding data to `src/Core/Assets/apps.builtin.json`. Do not add per-app `if` branches in `Scanner`.
- Support a new plugin manifest format by adding an `IPluginReader` and registering it in `PluginReaders.All`. Same pattern for context rules (`IContextRule` in `ContextBudget.Rules`, app's `context` field) and live limits (`ILimitProvider` in `LimitProviders.All`, app's `limits` field).
- Live limits: network calls only for apps the user turned on (Settings › Usage limits, with a consent dialog); at most one call per app per refresh interval (one minute minimum); each provider calls only its own host. Tokens stay in memory: never shown, logged, saved or refreshed; credential files are opened read-only.
- Update check: off until the user turns it on (Settings › Updates) or presses Check now; automatic checks at most once a day, only to api.github.com, nothing about the user sent. It never downloads or installs. A release is a GitHub release tagged `v<Version>` (`<Version>` in `Directory.Build.props`); drafts and pre-releases are ignored.
- Every API call or long operation shows `Busy` (spinner after 300 ms); a refresh keeps the last data on screen with a small spinner instead of blanking it. Errors replace the spinner with a message and Retry.
- Colors: Snowy Owl tokens (`snowy-owl/docs/TOKENS.md`) are defined once in `Theme`; users may override accent, hover and selection per theme (Settings › Appearance). Views use `W` resource keys through `W.Themed` (light/dark automatic). No hex anywhere else.
- Font and sizes: Appearance settings (default Inter → Noto Sans → platform default, 14 px, weights 400/500). Write sizes for a 14 px base through `W.Txt`/`W.Title`/`W.Size` so they scale; use `W.Body`/`W.Emphasis`/`W.Heading`, never fixed weights. Fonts are not bundled or downloaded.
- Levels are always listed highest priority first: System, Local, Project, User (`Levels.Order`).
- The scanner is read-only. Only `SkillManager`, `PluginManager`, `ConfigEditor` and `LogTrash` change anything, and only after the user confirms in a dialog. Removed skills are moved to the backup folder, never deleted. Saved config files are copied to the backups folder first and refused if they changed on disk. Plugin changes go through the app's own CLI, run without a shell, with whitelisted arguments. Logs are viewed read-only; deleting moves only files the app listed as logs to the Recycle Bin/Trash.
- Third-party UI code: AvaloniaEdit + TextMate (MIT) only inside `CodeView`.
- Catch only expected IO/JSON exceptions; never swallow broadly. Fatal errors are written to crash.log (`CrashLog`), then the app closes as before.
- A control kept in a field and shown again on a rebuilt page must be taken from its old parent first (`.Free()`); otherwise Avalonia throws "already has a parent" and the app closes.
- Items are records whose lists compare by reference: to find "the same" skill or plugin after a rescan use `ItemBrowser.Same`, not `==` or `Contains`.
- Keep one class per concern; keep files under about 250 lines. Split a larger class into partial files named `Class.Topic.cs` (e.g. `LogViewer.Search.cs`, `Widgets.Inputs.cs`).
- Commit messages (squash-merged PR titles) follow Conventional Commits: `feat:`, `fix:`, `perf:`, `refactor:`, `test:`, `build:`, `ci:`, `docs:`, `chore:`; they become the release notes.
- Verify with `dotnet build` before finishing. Report what could not be verified.

## Do not guess
Skill/config paths for apps without documented locations are best-effort. Mark them with a `notes` field in the JSON instead of presenting them as fact.
