# UI conventions

Short, checkable rules the user has asked for. Follow them in any new list, card or label. Product decisions are in `decisions.md`; sizes in `design-system.md`; text in `typography.md`.

## Names (use exactly)

| Where | Text |
|---|---|
| Side menu / page titles | Dashboard, Apps, Plugins, Skills, Commands & Agents, Servers, Hooks, Usage, Models, Logs, Health, Settings |
| Routes (`MainWindow.Go`) | `Dashboard`, `Apps`, `Plugin`, `Skill`, `Part`, `Mcp`, `Hooks`, `Usage`, `Models`, `Logs`, `Health`, `Settings`, `App:<id>`, `Project:<path>` |
| Pair order | "Plugins and skills" (cards, charts, text). Never "Skills and plugins" |
| Servers | MCP and LSP together; the Type filter says MCP / LSP |
| Dashboard Health tile and menu icon | `heart_pulse`; Usage = `data_pie`; Commands & Agents = `bot` |
| Capital letters | "Commands & Agents" (both capitalised); "Plugins and skills by author" |

## Lists (tables)

Build every list with `Table.Build(cols, rows)`; make columns with `Cols` (`Table.cs`) so the same kind of column has the same width on every page.

1. **Order of columns:** main value first (name, file, event, rule); then scope from biggest to smallest: **App → Level → Type**; then details (Version, Holds, Runs, Matcher, Path, Note, Source); then the button column last.
   - Plugins: Plugin, App, Level, Version, Path. Skills: Skill, App, Level, Invoked by, Path. Commands & Agents: Name, App, Level, Type, Path. Servers: Name, App, Level, Type, Runs, Note, Source. Hooks: Event, App, Level, Matcher, Runs, Note, Source. Config files: File, (Read by), Level, Type, Holds, Path.
2. **Widths:** fixed pixel columns scale with the font. `Widths.ColApp` 150, `ColLevel` 110, `ColType` 120, `ColVersion` 100, `ColStatus` 92 (an installed/not found pill), `ColCount` 80 (right-aligned numbers), `ColAction` 40 (one button), `ColActions` 112 (three). Name and Path columns are stars (`Cols.Name`, `Cols.Path`) and take the rest. Do not write a literal width for these; add a token if a new shared kind appears.
3. **Default sort:** A to Z on the first column, case-insensitive (`StringComparer.OrdinalIgnoreCase`), then App, then Level rank (`Levels.Rank`). Exceptions kept on purpose: Apps (installed first), Health (warnings first), Logs (newest first).
4. **Counts:** a column of counts uses `Cols.Count("Plugins")`, every one the same width; an app that is not installed shows `-` for 0 and a zero is in the muted color. A table with fixed columns that can be wider than the window (Apps) is wrapped with `Table.Scrollable(table, Table.MinWidth(cols))`: it scrolls sideways instead of cutting off the last columns (not for a virtual list, which scrolls inside the page).
5. **Rows** open the item (`Click`), the last column holds icon buttons (open file, open folder, copy path, remove), and an empty list says what is missing and, when filters exist, to clear them.

## Cards and pages

- A card with a title uses `W.Section(icon, title, body)`; a page is `Page(...)` with `H1` first.
- Page order on the Dashboard: tiles, usage strip, then the left column Plugins by app → What plugins add → Skills by app, right column by author and recent changes.
- Every API call or long operation shows `Busy`; a refresh keeps old data with a small spinner.

## Code style that reviews check

Braces on every `if`/loop; Allman; `_camelCase` private fields; file-scoped namespaces; license header on every `.cs` (`dotnet format` adds it); files under about 250 lines (split as `Class.Topic.cs`); `ConfigureAwait(false)` in Core only; build with 0 warnings; Conventional Commit messages (`feat:`, `fix:` …) because they become the release notes.
