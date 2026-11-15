# AGENTS.md

Source of truth for AI coding agents (Codex, Claude Code, others). `CLAUDE.md` imports this file; edit here only.

## Project
AI Stackprint: cross-platform desktop app (Avalonia 12, .NET 10, C# only, no XAML) that inventories AI apps, orchestrators, plugins, skills, config files and logs on this machine, by level (Project / Local / User / System).

## Commands
- Run: `dotnet run --project src/App`
- Build: `dotnet build -v q -nologo` (the solution `AiStackprint.slnx`: all projects; `-v q` prints only warnings, errors and the result: keep it, the full output is noise)
- Publish: `dotnet publish src/App -c Release -r <win-x64|linux-x64|osx-arm64> --self-contained false`
- Installer: `pwsh build/pack.ps1 -Runtime <win-x64|linux-x64|osx-arm64>` (Velopack, self-contained; on that platform). Releases: push tag `v<major>.<minor>.<patch>`: the version comes only from the tag (MinVer; not written in any file); `.github/workflows/release.yml` builds all three and publishes the GitHub release with notes from git-cliff (`cliff.toml`). See `docs/releasing.md`.
- Test: `dotnet test -v q --nologo` (all), or `dotnet test tests/AiStackprint.Core.Tests -v q --nologo` (services; builds Core only, no UI); some tests: add `--filter FullyQualifiedName~Part` (class or test name). If you add logic to `src/Core/Services`, add tests for it. UI tests run headless (Avalonia.Headless.XUnit, xUnit v3): mark them `[AvaloniaFact]`/`[AvaloniaTheory]`, including any test that only touches Avalonia types such as `Theme` (a plain `[Fact]` on another thread breaks the next UI test); `TestApp` starts the real app styles without a window or scan; helpers in `Ui` (show, find texts and buttons, click, wait for background work). Pass `TestContext.Current.CancellationToken` where a method takes a token.
- Performance: `dotnet run --project src/App -c Release -- --perf` opens every page, appends times, CPU and memory to `perf.log` and writes `perf-report.txt` (both next to settings.json) comparing with the run before; run it before and after a change that may affect speed (see Performance in `docs/architecture.md`).
- Format: `dotnet format` before committing. Style and analyzers follow dotnet/aspnetcore's `.editorconfig`: braces on every `if`/loop, Allman braces, `_camelCase` private and internal fields (static too), PascalCase constants, `var`, file-scoped namespaces, System usings first, no unused usings, `ConfigureAwait(false)` in Core (not in UI code). Sonar's default C# rules also run on build (`SonarAnalyzer.CSharp`, a build-only package); the few turned off are listed with reasons in `.editorconfig`, and a single justified exception uses `#pragma warning disable Sxxx // reason`. Analyzer and style warnings show on build; keep it at 0 warnings.
- Every `.cs` file starts with the license header (`file_header_template` in `.editorconfig`; `dotnet format` adds it). The license is MIT (`LICENSE`).
- SDK: .NET 10 (LTS), pinned in `global.json`. The app version comes from git tags (MinVer); builds between tags are `<next>-alpha.0.<commits>`. Package versions are set once in `Directory.Packages.props` (exact versions; projects list packages without versions). Shared build settings: `Directory.Build.props`.

## Layout
Large files: search them, do not read them whole: `src/App/Assets/logos.json` (57 KB) and `icons.json` (21 KB) are path data (search by name), `src/Core/Assets/apps.builtin.json` (24 KB, search by app id), `.editorconfig` (19 KB, search by rule id), `docs/architecture.md` (read the section you need).
- `src/App/AiStackprint.App.csproj` (the app, builds `AiStackprint.exe`: `Ui`, `Program.cs`, `Assets`) references `src/Core/AiStackprint.Core.csproj` (models and services, no Avalonia). Services must not depend on UI code; the build enforces it.
- `src/Core/Assets/apps.builtin.json`: built-in app definitions (data, embedded resource in Core)
- `src/App/Assets/icons.json`, `src/App/Assets/logos.json`: UI icons and app logos as path data (embedded); see `docs/architecture.md`
- `src/Core/Models`: records and definition classes
- `src/Core/Services`: generic, read-only `Scanner` and the services around it (catalog, settings, plugin readers, logs, context, limits, health, updates, crashes); `src/App/Ui`: views and widgets (`MainWindow` routes pages, `W` widget helpers, `Theme` colors and fonts, `Busy`, `Toast`, `CodeView`). One-line description of every file: the file map in `docs/architecture.md` (update it when you add or rename a file).
- `tests/AiStackprint.Core.Tests`: xUnit v3 tests for Core; `tests/AiStackprint.App.Tests`: headless tests for UI code
- `build/pack.ps1`: Velopack installer for one platform; `.github/workflows/release.yml`: release on a version tag
- `docs/`: architecture and how-to guides (`releasing.md`: versions, tags, installers)
- `docs/decisions.md`: agreed product and UI decisions; read before UI work
- `docs/design-system.md`: tokens (spacing, radius, type, icon and component sizes) and shared components; read before UI work

## Rules
- Support a new app by adding data to `src/Core/Assets/apps.builtin.json`. Do not add per-app `if` branches in `Scanner`.
- Support a new plugin manifest format by adding an `IPluginReader` and registering it in `PluginReaders.All`. Same pattern for context rules (`IContextRule` in `ContextBudget.Rules`, app's `context` field) and live limits (`ILimitProvider` in `LimitProviders.All`, app's `limits` field).
- Live limits: network calls only for apps the user turned on (Settings › Usage limits, with a consent dialog); at most one call per app per refresh interval (one minute minimum); each provider calls only its own host. Tokens stay in memory: never shown, logged, saved or refreshed; credential files are opened read-only.
- Update check: off until the user turns it on (Settings › Updates) or presses Check now; automatic checks at most once a day, only to api.github.com, nothing about the user sent. It never downloads or installs. A release is a GitHub release tagged `v<major>.<minor>.<patch>`; drafts and pre-releases are ignored. Show the version with `UpdateCheck.Label` and compare with `UpdateCheck.Current` (the assembly version stays at major.0.0 under MinVer).
- Every API call or long operation shows `Busy` (the loading icon shows at once; every control whose data is not ready shows it); a refresh keeps the last data on screen with a small spinner instead of blanking it. Errors replace the spinner with a message and Retry.
- Colors: Snowy Owl tokens (`snowy-owl/docs/TOKENS.md`) are defined once in `Theme`; users may override accent, hover and selection per theme (Settings › Appearance). Views use `W` resource keys through `W.Themed` (light/dark automatic). No hex anywhere else.
- Font and sizes: Appearance settings (default Inter → Noto Sans → platform default, 14 px, weights 400/500). Write sizes for a 14 px base through `W.Txt`/`W.Title`/`W.Size` so they scale; use `W.Body`/`W.Emphasis`/`W.Heading`, never fixed weights. Fonts are not bundled or downloaded.
- Sizes, spacing, radii, type and icon sizes: tokens in `src/App/Ui/Tokens.cs` (`Space`, `Radius`, `TextSize`, `IconSize`, `Stroke`, `Sizes`, `Widths`, `Heights`; see `docs/design-system.md`). No literal sizes in views: `DesignTokenTests` fails on them. Cards with a title use `W.Section` (one header for all cards).
- Levels are always listed highest priority first: System, Local, Project, User (`Levels.Order`).
- The scanner is read-only. Only `SkillManager`, `PluginManager`, `ConfigEditor` (saves, and the AGENTS.md export after its preview) and `LogTrash` change anything, and only after the user confirms in a dialog. Removed skills are moved to the backup folder, never deleted. Saved config files are copied to the backups folder first and refused if they changed on disk. Plugin changes go through the app's own CLI, run without a shell, with whitelisted arguments. Logs are viewed read-only; deleting moves only files the app listed as logs to the Recycle Bin/Trash.
- Third-party UI code: AvaloniaEdit + TextMate (MIT) only inside `CodeView`.
- Catch only expected IO/JSON exceptions; never swallow broadly. Fatal errors are written to crash.log (`CrashLog`), then the app closes as before. On the next start `CrashNotice` offers Report on GitHub: a prefilled new-issue link in the browser with the home folder and user name removed; the app itself sends nothing.
- A control kept in a field and shown again on a rebuilt page must be taken from its old parent first (`.Free()`); otherwise Avalonia throws "already has a parent" and the app closes. The view's `Leave()` (called by `MainWindow.Leave` when another page opens) must replace such controls and drop callbacks into the page, or the old page stays in memory (see Performance in `docs/architecture.md`).
- Items are records whose lists compare by reference: to find "the same" skill or plugin after a rescan use `ItemBrowser.Same`, not `==` or `Contains`.
- Keep one class per concern; keep files under about 250 lines. Split a larger class into partial files named `Class.Topic.cs` (e.g. `LogViewer.Search.cs`, `Widgets.Inputs.cs`).
- Commit messages (squash-merged PR titles) follow Conventional Commits: `feat:`, `fix:`, `perf:`, `refactor:`, `test:`, `build:`, `ci:`, `docs:`, `chore:`; they become the release notes.
- Verify with `dotnet build -v q -nologo` before finishing. Report what could not be verified.

## Do not guess
Skill/config paths for apps without documented locations are best-effort. Mark them with a `notes` field in the JSON instead of presenting them as fact.
