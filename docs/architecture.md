# Architecture

```
src/Core/Assets/apps.builtin.json ┐
%APPDATA%/AiStackprint/apps.json ─┴─> AppCatalog.Load() ──> List<AppDefinition>
                                                        │
 project folders (user picks) ──────────────────────────┤
                                                        v
                                   Scanner.Collect(defs, projects)
                                     ├─ detect folders      -> AppInfo (installed?)
                                     ├─ Roots / ProjectRoots -> skills (SKILL.md or flat *.md)
                                     └─ IPluginReader        -> plugins (+ their skills)
                                                        │
                                                        v
                                        ScanResult(Items, Apps) -> UI
```

## File map
- `src/Core/Services`: `AppCatalog` (built-in + user `apps.json`), `SettingsStore` (`settings.json`), `Scanner` (generic, read-only; `.Skills`: skill folders), `PluginReaders` (per-format), `SkillManager` (add/remove skills), `PluginManager` (runs the app's plugin CLI), `LogReader` (finds and tails log files), `LogParsers` (session logs → readable steps; one file per parser: `ClaudeSessionParser`, `CodexSessionParser`), `LogSearch`, `LogSummary`, `LogFolderWatcher` (watches an app's log folders), `LogTrash` (deletes logs to Recycle Bin/Trash), `ConfigEditor` (validated, backed-up saves of config files), `Contrast` (WCAG ratios), `SkillDoc` (front matter, links), `SkillCheck` + `Health` (checks), `ContextBudget` + `ContextRules` (what each app loads into context per project), `LimitsService` + `LimitProviders` (live plan and limits), `ScanDiff` (changes since the last scan), `UpdateCheck` (latest GitHub release vs this version), `CrashLog` (crash.log), `CrashReport` (prefilled GitHub issue link for the last crash)
- `src/App/Ui`: `MainWindow` (nav + routing; `.Actions`: refresh, rescan, add project/app; `.Nav`: history, shortcuts, limits timer; `.TitleBar`: custom title bar: name, back/forward; F11 full screen), `CommandPalette` (Ctrl+K), `DashboardView`, `AppsView`, `AppDetailView` (+ `UsageCard`, `LimitsView`), `AppActions`, `LibraryView` (plugins/skills page) + `ItemBrowser` (list, filters, bottom panel) + `DetailPanel` + `FileView`, `ProjectView` (+ `ContextSection`), `HealthView`, `LogsView`, `LogViewer`, `LogSummaryCard`, `ReadableLog`, `Levels` (order + explanations), `SettingsView`, `AppearanceSection`, `ColorSettings`, `CodeView` (the only AvaloniaEdit user), `ConfigPanel`, `Dialogs`, `AddAppDialog`, `Table`, `W` (widget helpers), `Busy` (the one loading indicator), `MemoryTrim` (debounced GC after a page or the last code view closes), `UpdateView` (Settings › Updates, new-version notice), `CrashNotice` (after a crash: report, open crash.log), `Toast`, `Theme` (colors + fonts), `Icons`, `Ctx`

## Levels
- System: managed or machine-wide folders (`level: "System"`, usually with `os`)
- User: folders under the home directory
- Project: `projectRoots`, resolved against each folder added in the UI

## Extension points
| Need | Change |
|---|---|
| New app or orchestrator | JSON entry, or "Add app" in the UI |
| New skill layout | `ScanRoot.Layout` value handled in `Scanner.ReadRoot` |
| New plugin manifest | `IPluginReader` implementation + register |
| New screen | class in `src/App/Ui`, add a nav button in `MainWindow` |

## Screens
- Dashboard, AI apps (list), app page (`App:<id>`), Plugins, Skills, project page (`Project:<path>`), Health, Settings. `MainWindow.Go` routes and keeps back/forward history (Alt+←/→, mouse back/forward, and ← → buttons in the custom title bar, after the app name; F11 toggles full screen, no maximize button); views get a `Ctx`. The top bar shows where you are (the list part is clickable) and Search (Ctrl+K: pages, apps, projects, skills, plugins, actions). F5 rescans (on the Logs page: re-reads the files); Esc closes the bottom panel.
- Lists are flat tables (`Table.Build`), not trees.
- Project page: "Loaded into context" (below), the instruction/settings files, and the skills that apply in the project (that project's own rows + every User/System row) in the same list as the Skills page.
- Dashboard: tiles (apps, plugins, skills, health warnings) open their page; Plan & limits; skills and plugins by app (stacked by level) and by author (top 10 + Other; plugin manifest author, else SKILL.md `author`/`metadata.author`, else the marketplace; skills with none are "Local"); a bar opens the list filtered to it. "Changed since …" lists skills, plugins and config files added, removed or edited since the scan before (`ScanDiff`, saved as `last-scan.json` next to settings.json).
- AI apps page: search, Status (All / Installed / Not found), category check boxes, Group by Category with collapsible headings (Expand all / Collapse all).

## Skills and Plugins pages
- `ItemBrowser`: full-width list (name, app link, level, invoked by or version, full path wrapped with the name part emphasized, row icons: open folder, copy path, remove). Search covers name, description, path and author. One instance per page, so search, filters and selection are kept.
- Clicking a row opens `DetailPanel` at the bottom (drag the splitter to resize, the height is saved; double-click it to maximize; Esc or × closes). Left: description, app (link), invoked by, from, plugin, author, the other front matter, size (characters, ≈tokens, lines), checks. Right: Content (SKILL.md or a plugin's README rendered by `MarkdownView.Document`, or Source) and Files. Links to files inside the folder open in the panel with Back; folders open Files; web links open the browser; a link that leaves the folder asks, then opens that place in the file manager.
- Plugins: version shown as is ("2.5.1"); the manifest (`.claude-plugin/plugin.json`) gives description and author; "Contains" lists skills, commands and agents and flags hooks and MCP servers (they run programs). Remove runs the app's CLI (`claude plugin uninstall`) as before.

## Context per project (`ContextBudget`, `ContextSection`)
- One `IContextRule` per app (`context` in apps.builtin.json). Each returns the files the app reads in a project, in load order, with when they load (at launch, imported, on demand, matching files, agent decides, when mentioned, ignored), size and notes; `Start in…` picks the folder a session starts in.
- Claude Code: CLAUDE.md / .claude/CLAUDE.md / CLAUDE.local.md from the start folder and every folder above, user file and rules, managed policy, @imports (four hops; code spans, fences and quotes skipped; missing, repeated, too deep and outside-the-project imports reported), auto memory (first 200 lines or 25 KB), `.claude/rules` with `paths:` for matching files only, CLAUDE.md in sub-folders on demand. Warns above 40,000 characters.
- Codex: global AGENTS.override.md / AGENTS.md (not counted), then one file per folder from the git root to the start folder (override, AGENTS.md, `project_doc_fallback_filenames`), cut at `project_doc_max_bytes` (32 KiB default, from config.toml) — reported, since Codex cuts silently.
- Gemini CLI (context.fileName, @imports, sub-folders on demand), Copilot (copilot-instructions.md, nearest AGENTS.md, `.instructions.md` by `applyTo`), Cursor (.mdc by alwaysApply/globs/description; plain .md in .cursor/rules is ignored; 500-line guideline). No limit is shown where none is documented.

## Health (`Health`, `SkillCheck`)
- Each SKILL.md against agentskills.io: front matter present and closed, name 1–64 lowercase/hyphens and same as its folder, description 1–1,024, compatibility ≤ 500, body under 500 lines, links broken or leaving the folder, references that link on further.
- Claude Code's skill list: names + descriptions of skills the agent can load, against 8,000 characters (its fallback budget). Same skill name at several levels; plugins whose folder is missing, installed in several scopes, or with hooks / MCP servers. Computed in the background after each scan; the nav shows the warning count.

## Plan & limits (`LimitsService`, `LimitProviders`, `LimitsView`)
- Off by default per app; turning it on shows what is read and where it is sent. Refresh interval in Settings (minutes, at least 1); pages show the last reading (“Updated 40 s ago”) and request a refresh only when due; one call at a time per app. 401/403 → "sign in again" and no further calls until the credential file changes; 429 → waits (Retry-After, at most 15 min); offline → keeps the last reading. Expired tokens are not refreshed.
- Claude Code: `.credentials.json` → `GET api.anthropic.com/api/oauth/usage` (5-hour, weekly, per-model, extra usage; plan from subscriptionType/rateLimitTier). Codex: `rate_limits` in its session logs (no network); with live limits on and the log older than 10 min, `GET chatgpt.com/backend-api/wham/usage`. Copilot: Copilot CLI config.json or editor apps.json token → `GET api.github.com/copilot_internal/user`. Cursor: state.vscdb (read-only) → `cursor.com/api/usage-summary` (shape not published: only recognized figures are shown). Gemini CLI: oauth_creds.json → cloudcode-pa loadCodeAssist + retrieveUserQuota. All undocumented: replies are read leniently.
- "Full about HH:mm at this pace" from the rise between two readings.

## Changing things
- Skills: `SkillManager.Install` copies a folder with `SKILL.md`; `SkillManager.Remove` moves it to `%APPDATA%/AiStackprint/removed/<timestamp>/`. System-level and plugin-provided skills cannot be removed here.
- Plugins: `PluginManager` runs `pluginCommands` from the app definition (Claude Code: `claude plugin install|uninstall`) and shows the output.
- Apps: `AppCatalog.RemoveUser` deletes a custom entry from `apps.json`; `settings.json` holds hidden apps.

## Viewing and editing files
- `CodeView` wraps AvaloniaEdit with TextMate grammars (json, markdown, yaml, ini for toml, log). Light+/Dark+ follows the app theme.
- Config files: read-only until Edit; Save validates JSON (comments/trailing commas allowed; other formats unchecked), refuses if the file changed since it was opened, copies the original to `%APPDATA%/AiStackprint/backups/<timestamp>/`, keeps the BOM choice. Files over 2 MB stay read-only.
- Logs page (`Logs:<id>`):
  - Summary (7 d / 30 d / all): files, size, activity, error/warning lines, and for session logs sessions, prompts, tool calls, top tools, sub-agents, tokens, models. Computed in the background; parsed sessions cached by path+size+time.
  - Files: newest first; delete one, selected, or "older than 7/30/90 days" → Recycle Bin (Windows) or Trash (macOS `~/.Trash`, Linux freedesktop Trash). Only files the app listed can be deleted; recently modified files and session logs get a warning.
  - Watcher (`LogFolderWatcher`): the selected app's log folders (by pattern, sub-folders when the source has depth), events batched for 250 ms, plus a 10 s stat of the newest file. New, removed or renamed files find the list again; a file that grows gets its size and time updated in place. Leaving the Logs page releases the viewer, summary, watcher and grammars and compacts memory. The open file reloads when Follow is on; otherwise a "New lines" button appears. New steps (Conversation) get an amber fill behind the text and a solid ring (Raw: amber lines) that fade out over Settings › Appearance › New log lines (default 10 s, 0–60, 0 = off; `fadeSeconds` in settings.json).
  - Viewer toolbar: search, Follow, Raw/Conversation, open file, open folder. No Reload (F5 reads again); the header says only when part of a large file is left out (size and time are in the file list).
  - Conversation: everything reads left to right; the assistant's messages leave 20 px free on the right, yours are indented 20 px from the left. A step whose opened text would equal its one-line header has no arrow.
  - Text in the Conversation view goes through `TextDecode.Prepare`: JSON (also inside a JSON string) pretty-printed, `\uXXXX`/`\n` escapes decoded, ANSI shell colors shown as colored runs (palette follows the theme), inline image data shortened; a "Show original" button undoes it per step.
  - Viewer: Raw = last 256 KB in `CodeView` (line numbers optional, off by default). Conversation = the last 8 MB streamed and parsed by an `ILogParser`; code fences, tool inputs and tool results are highlighted (results by the file a Read returned, or JSON/diff detection), built lazily when expanded.
  - Search covers the whole file (case, regex, "only matching lines"); results list with line numbers; clicking jumps to the line, loading the part of the file around it when it is outside the tail.

## Crashes
`CrashLog` writes any error that closes the app (and failed background work nobody waited for) to `crash.log` next to settings.json, with the version and OS; it does not keep the app running. On the next start `CrashNotice` shows a card once: Report on GitHub opens a prefilled new-issue page (`CrashReport`: last entry, home folder and user name removed, cut to fit the link); the user submits it in the browser, the app sends nothing.

## Appearance
Settings › Appearance: font family (installed fonts), base size 12–18, body and emphasis weight (one list, 300–700), log & code font and size (typed number, 10–18, default 14; search results in the log viewer use the same size), log line numbers, and accent / hover / selection colors for each theme (hex or Snowy Owl swatches; a hint shows when contrast drops below 7.1:1). `Theme.Appearance` applies them; the UI is rebuilt so sizes take effect.

## Theming
- Colors: Snowy Owl tokens in `Theme` (light `#F8F9FA`/`#34383C`, blue accent; dark `#18191B`/`#F8F9FA`, orange accent). `Theme` sets both `W` resource keys and the Fluent palette so built-in controls match.
- Level colors: Project = orange, Local = orange ring, User = blue, System = warning yellow. Order everywhere: System → Local → Project → User; ⓘ next to level filters explains each.
- Status: Snowy Owl report style — success-colored outline and tint; in dark theme the text uses the normal foreground for contrast.
- Font: Inter, Noto Sans, platform default. Monospace: JetBrains Mono, Cascadia Mono, Consolas.
- Theme: System (default, follows the OS), Light or Dark; saved in `settings.json`.

## Icons
- `src/App/Assets/icons.json`: UI icons (Fluent UI System Icons, 24 regular). `src/App/Assets/logos.json`: app logos (LobeHub Icons, Simple Icons). Both embedded.
- Format: `{ "name": [ { "d": "<path data>", "o": <opacity, optional> } ] }` on a 24x24 grid; a leading `F1 ` means nonzero fill.
- `W.Icon(name)` and `W.AppLogo(logo, category)` render them in the inherited text color, so they follow the theme. Monochrome by design (no brand hex).
- The sidebar collapses to an icon rail (toggle at the top); collapsed items show tooltips.

## Usage card (app detail page)
- `UsageCard` appears for apps whose session logs have a usage parser (`ILogParser.Usage`: Claude Code, Codex; `ILogParser.UsageFile` for a database: OpenCode). It shows tokens by type (input, output, cache write, cache read), an estimated cost, a per-day chart (tokens or cost, last 90 days at most), and totals by model and by project, for 7 days / 30 days / all.
- `UsageStats` reads each file once (newest 8 MB, streamed) into small per-day buckets, cached by path + size + time. Claude Code replies that are written on several lines are counted once (message id + request id). Codex totals are running counters, so each event is the difference from the one before.
- OpenCode (`OpenCodeUsage`, read-only SQLite): one event per assistant message in `message`, and one per session in `session_v2` (newer versions keep no per-message tokens). Its output excludes reasoning, so reasoning is added to output. Its own `cost` is used when above 0 (0 also means "no price in OpenCode"), else `prices.json`.
- Cost: `Pricing` reads `prices.json` (next to `settings.json`, created with a few defaults; "Edit prices" opens it). USD per 1M tokens; a model gets the price of the longest key its name contains; models without a price show tokens only. Prices are estimates from public list prices, so check them.
- An all-zero price entry means "no price yet": "Edit prices" adds one per unpriced model so only the numbers need filling in. The chart uses its own theme keys (`Ap.ChartIn/Out/Write/Read`) so series differ in light and dark.
- Limits (top of the card): `UsageStats.Live` gives use in the last 5 hours / 7 days and the current 5-hour block (a block starts at the first message after a 5-hour gap, on the hour). `ILogParser.Limits` returns percent used and reset time when the app logs them (Codex `rate_limits`); Claude Code does not log them, so only use is shown.

## Conversation view extras
- `ContextBlocks` splits injected text (`<INSTRUCTIONS>`, `<environment_context>`, `<system-reminder>`, `<!-- name:begin -->…<!-- name:end -->`) into collapsed "Injected" areas inside the message. `MarkdownLite` + `MarkdownView` render headings, lists, quotes, rules, tables and bold/italic/code/links as selectable text; code fences still use `CodeView`.
- `AgentTimelines` + `AgentGraph` draw a lane per sub-agent (Claude: `agentId`) with the branch and merge points and the periods the main session waits. `LogEntry.Agent` carries the id.
- Plain `Txt` text is not selectable; right-click copies it (`MainWindow.CopyMenu`). Use `W.Sel` for text outside click targets (detail panels).
- Skills / Plugins pages and an app's Skills section: level check boxes (all checked by default); skills also Invoked by as one choice (All, Agent only, User only, Agent and User, with counts that follow the level and source filters), From and Group by (Invoked by / From / both, collapsible headings). The last checked box cannot be unchecked. Only values present are offered; a filter with one value is left out; filters reset when another app opens. UI in `SkillFilter`, rules in `SkillMatch`.
- Who can invoke a skill is a list (`Item.Invokers`: Agent and/or User from SKILL.md front matter), shown as "Agent only", "User only" or "Agent and User" in the filter, table and group headings.
- AI apps page: category check boxes (plural labels, counts follow the search), kept while the app runs.
- Rule: one value per item (level, category) → check boxes; a combination per item (invoked by) → one choice. Filter rows use `FilterRow` (fixed-width label column).
- Alignment: `Row()` centers its children vertically; `Pills()` spaces with the panel (one line is 32 px); `SearchBox()` centers text and icon.
- Dots beside text (`WithDot`: level, chips, legends) are an inline of the TextBlock standing on the baseline, so they line up with the letters.
