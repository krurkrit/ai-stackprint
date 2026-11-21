# Recipes

Step lists for the common changes, so an agent does them without exploring. Each lists the files to touch, in order. Always finish with `dotnet build -v q -nologo` (0 warnings), `dotnet test -v q --nologo`, `dotnet format` (then `git checkout` any file the formatter changed that you did not edit). Find the area in `feature-map.md` first; do not read big data files whole (`logos.json`, `icons.json`, `apps.json`, `models.json`: search by name).

## Add or change an app (data only)

1. `default/apps.json`: add or edit the entry (schema in `adding-apps.md`). Paths that are not documented by the vendor get a `notes` field; do not present them as fact.
2. Logo: a key in `src/App/Assets/logos.json` (see "Icons and logos" below).
3. Test: extend `tests/AiStackprint.Core.Tests/AppCatalogTests.cs` or `ScannerTests.cs` if behaviour, not just data, changed. No per-app `if` in `Scanner`.

## Add a plugin manifest format, context rule or live-limit provider

- Plugin format: implement `IPluginReader` (`PluginReaders*.cs`), register in `PluginReaders.All`, test in `PluginReadersTests`.
- Context rule: `IContextRule` in `ContextBudget.Rules` (`ContextRules*.cs`), the app's `context` field; test in `ContextTests`.
- Live limit: `ILimitProvider` in `LimitProviders.All`; network only for apps the user turned on, one call per interval, only its own host, tokens in memory only; test in `LimitsTests`.

## Add a page

1. View class `src/App/Ui/<Name>View.cs` with `Build()` returning `Page(H1("<Title>"), …)`; keep a field control through rebuilds with `.Free()` and implement `Leave()` to drop callbacks (see Performance in `architecture.md`).
2. `MainWindow.cs`: field, route in the `_view switch` in `MainWindow.Actions.cs`, nav button in `RefreshNav` (icon key from `icons.json`), `Leave` case.
3. `CommandPalette.cs` page list; `MainWindow.Perf.cs` page list.
4. `tests/AiStackprint.App.Tests/PagesTests.cs`: add `("<Title>", () => new XView(x).Build())` to the page list; mark UI tests `[AvaloniaFact]`.
5. Docs: a row in `feature-map.md`, the route in `conventions.md`, a section in `architecture.md`, and the file in its file map.

## Add a list (table) or a column

1. Columns: `Cols.Name/App/Level/Type/Version/Count/Path/Action(s)` from `Table.cs`; order and widths per `conventions.md`. A new shared kind of column → a `Widths.Col…` token in `Tokens.cs` and a `Cols` factory.
2. Rows: build `TableRow(cells[], click, selected)`; the number of cells equals the number of columns.
3. Sort A to Z on the first column (case-insensitive), then App, then Level.
4. Empty text says what is missing; filters get a "Clear" hint.
5. Test the page title and loading in `PagesTests`; test search or filter logic in the matching Core test, not in UI.

## Add a setting

1. `default/settings.json`: key, default, comment (the only place defaults are written; a test fails otherwise).
2. `Settings` class property; read defaults with `SettingsStore.Defaults()`, never `new Settings()`.
3. UI: a row in the right `*Section` (Settings page). Values the app remembers that are not preferences go to `UiState` (`state.json`).
4. Renaming a key or changing its type or meaning needs a new format version and an upgrade step (`VersionedJson`); adding or removing a key does not.
5. Test in `SettingsStoreTests`.

## Add a service in Core

`src/Core/Services/<Name>.cs`: no Avalonia, `ConfigureAwait(false)` on awaits, catch only expected IO/JSON exceptions, never swallow broadly, read-only unless it is one of the writers named in `AGENTS.md` and gated by a confirm dialog. Add tests in `tests/AiStackprint.Core.Tests` (run alone with `dotnet test tests/AiStackprint.Core.Tests -v q --nologo`). Keep a file under about 250 lines; split as `Class.Topic.cs`.

## Icons and logos

- UI icon: path data in `src/App/Assets/icons.json` under a snake_case key. Source: Fluent UI System Icons, 24 px regular (`https://github.com/microsoft/fluentui-system-icons`, `assets/<Name>/SVG/ic_fluent_<name>_24_regular.svg`). Take the SVG `d`, prefix `F1 `, one `{"d": …}` per `<path>`. Use the key in a call like `Go("heart_pulse", …)` or `Icon("name", IconSize.S)`.
- App logo: `logos.json` (LobeHub Icons, Simple Icons), referenced by the app's `logo` field.
- Preview all: generate an HTML sheet from the two JSON files (strip the `F0/F1` prefix), do not commit it.

## Add or change a colour, size or font

Colours: `Theme.cs` only (color tokens, light and dark palettes, user overrides); views use `W` resource keys through `W.Themed`. Sizes: `Tokens.cs` and `design-system.md`. Fonts and weights: `typography.md`. `DesignTokenTests` fails on a literal size.

## Model data

Never edit `default/models.json`. Change `tools/ModelData` (`Generator`, `Checks`, `Writer`) and its tests in `tests/ModelData.Tests`. The weekly `models.yml` workflow regenerates the file and opens the `bot/models-update` PR (see `feature-map.md`). The user's own changes live in `~/.ai-stackprint/models.json`.

## Release

Push tag `v<major>.<minor>.<patch>`; the version comes only from the tag (MinVer). `release.yml` builds all three platforms with Velopack and publishes notes from git-cliff, which reads Conventional Commit titles. See `releasing.md`.

## Cheap-work habits for agents

- Search by symbol or heading, then read only that range. Large files are listed in `AGENTS.md`.
- Edit with exact-string replacements; run the formatter once at the end.
- Build the one project you changed while iterating (`dotnet build src/Core -v q -nologo`), the whole solution once before finishing. If `AiStackprint.exe` is running, the app project cannot overwrite its output: ask the user to close it.
- Run only the affected tests while iterating (`--filter FullyQualifiedName~Part`), all tests before finishing.
- Report what you could not verify (for example, a look at the running UI).
