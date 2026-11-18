# Product and UI decisions

Decisions agreed with the owner. Read before UI work. Rules in `AGENTS.md` win on conflict.

## General
- Name is "AI Stackprint" everywhere, including the Task Manager process name, with the app icon on the process group. Never "AiStackprint" in UI, and no "Owl" in the name (Snowy Owl is only the theme).
- Use US English in the app and docs ("Color", not "Colour").
- If the app has no data for something (no logs, no prices), remove that element from the UI. Don't show "n/a".
- Use flat dashboard-style lists, not expandable trees. No list density toggle.
- Keep the app fast and light. Memory should drop back after leaving the Logs page (baseline is about 100 MB).
- Show a loading indicator for every API call and long process (`Busy`).

## Typography and appearance
- Inter is the UI font, with lighter weights. All menu items use weight 500.
- Appearance settings: fonts, sizes, weights, log font and size, log line numbers (hidden by default), and accent, hover and selection colors per theme. There is no Theme row; the theme switch (System, Dark, Light in one rounded group, the chosen one filled) is at the right of the title bar, before the window buttons (on macOS, at the right edge), so it looks the same whether the side menu is open or collapsed.
- Title bar, left to right: the menu toggle (expand/collapse the side menu), the name, back and forward; the search box (Ctrl+K) centered in the window; the theme switch and the window buttons. The side menu starts with its pages; it has no toggle or search of its own.
- UI and log font sizes both use a typed number input, not a dropdown. Log size defaults to 14, range 10–18.
- Body and Emphasis weights share one list.
- Inputs (text, number and search boxes, drop-downs, check boxes, radio buttons) share one height, and one-line text sits in the middle. Multi-line inputs start their first line where a one-line input shows its text and leave the same gap below the last line. The gaps come from the font's line height (`Theme.Inputs`), so this holds at any font and size; do not set a fixed `MinHeight` or padding on an input.
- Dark-theme status is a green outline with normal foreground text, not a filled badge.

## Dashboard and Apps
- Every card header is the same: icon on a tinted square, then the title in the heading weight (`W.Section`). Stat tiles use it too.
- Dashboard shows summary charts of plugin and skill counts by app and by author, and what plugins add (skills, commands, agents, hooks, MCP and LSP servers, each out of all plugins) with their top keywords; a bar or keyword opens the Plugins list searched for it (has:mcp, keyword:"x"). Don't add an "AI apps and orchestrators" list there; the Apps page already has it.
- Dashboard top: "Apps by category" (2 x 2) on the left: only installed apps, the count per category (not "n of total": whether every known app is installed does not matter, only which kinds of AI apps the user has). On the right, Plugins and Skills on the first row, MCP servers (by level) and Health warnings on the second. No "Apps installed" tile.
- The page and menu item are "Apps", not "AI apps": everything in the app is about AI, and the other menu items are one word.
- Apps page has a Status filter and Group by Category, collapsible per category, like the Dashboard.
- The app detail page shows per-app usage where logs have the data: tokens, a per-day chart, a cost estimate, and more.
- Live usage limits (limit %, plan, and so on) come from each app's saved token plus its API. At most one call per app per refresh interval, with cached data shown in between. The interval is a setting in minutes, default 1.

## Plugins and Skills
- Plugins page:
  - Version shows as "2.5.1", not "Version 2.5.1".
  - Each row has icons to open the folder, copy the path, and remove the plugin.
  - Full-page list with the full path and no mandatory right panel.
  - The App column links to the app; there is no "Open app" button.
  - Lists the plugins and extensions of every app that has them, not only Claude Code. Editor extensions (VS Code layout) and extensions an app calls by that name keep the word in the level column ("Extensions"). Disabled ones are listed, marked "disabled". Install and remove only through an app's own CLI (Claude Code today).
- Descriptions never show raw HTML or Markdown marks: lists and the detail panel show one line of plain text; rendered SKILL.md and README files show their HTML as Markdown (badges as their alt text). Chat text in logs is shown as written.
- Skills page matches the Plugins page. Clicking a skill opens a bottom panel (like Logs) with all skill info and rendered markdown. Linked references and scripts in that panel can be opened and viewed, with markdown supported.
- Skill filters:
  - Level is check boxes for System, Local, Project and User, all checked by default.
  - Invoked by has four options: Agent or User (all), Agent only, User only, Agent and User.
  - App category is also a filter.
  - The same filters, and the same search, appear on an app's Skills section.
- Search on the Skills and Plugins pages: words match separately (all must match), "quotes" match a phrase, and field:value (name, desc, author, app, level, keyword or tag, path, has) matches one field; author:"Jane Doe" in quotes matches the whole value. Best matches first (name, then plugin keywords, author, description, path), matches highlighted. A dashboard author bar opens the list with author:"name", counting authors the same way as the chart (`ItemAuthor`).

## Project page
- Shows the skill list with the Skills-page features.
- Shows context limits (e.g. Codex `project_doc_max_bytes` for AGENTS.md), with warnings when a file goes over.
- Shows CLAUDE.md `@import` links, and which files load at session start and which don't.
- Checks the project's AI files: rules that never load (their patterns match no file), text loaded twice at launch, empty files, broken links, secrets in config files, `bypassPermissions` and `Bash(*)`. Findings also appear on Health under Projects.
- Lists the MCP servers and hooks the project's config files start, with unpinned packages flagged. Secret values are never shown, only where they are (JSON path); flags like `--api-key` show `•••`.
- Only documented limits are shown (Claude Code 40,000 characters, Codex 32 KiB, Cursor 500 lines); apps without one show sizes only.
- Warns about personal files (CLAUDE.local.md, settings.local.json, …) that git does not ignore, read from the ignore files without running git.
- AI folders card: the hidden folders apps keep in the project, what is inside, size; then the commands and agents that apply (project and user level).
- "Loaded into context" has By app / By file: the by-file view shows which app loads each file and how.
- Info (not warnings): a CLAUDE.md/GEMINI.md/QWEN.md next to AGENTS.md that never mentions it; apps that read no file here; ignore-file patterns that match nothing; stale paths in instruction files. Warning: a hook that runs a script that does not exist.
- "Claude Code settings" card (only when a settings file exists): permission rules and settings by level, highest priority first; a deny anywhere wins; env shows names only.

## Servers (MCP and LSP)
- MCP and LSP servers share one page, "Servers", with a Type column and pills: both are programs an app starts with your rights and have the same columns; they differ in purpose (tools for the model, code intelligence for the agent), and LSP servers are few (mostly from plugins). Servers a plugin adds are rows of that plugin ("from plugin X").
- One page lists every MCP server in every app's config files, at every level, highest first: what runs or where it connects, and notes (not pinned, key in plain text, env names, disabled). Warnings alone on request; a search narrows it.
- Read-only and offline: servers are never started or asked for their tools. Secret values are never shown.

## Config files
- A config file is the source, not the thing: the app page's and project's "Instructions & settings" tables have a "Holds" column ("2 MCP servers · 1 hook · model claude-opus-5-5 · 3 permission rules", "12 patterns" for an ignore file), so a settings file explains itself. Servers and hooks link to their page. How many project files an ignore file hides stays in the project's Checks card (it needs the file list).

## Hooks
- Hooks are their own page, not part of the config files: each row is one command or prompt with its event, matcher, app, level and source, because a hook runs with your rights on every matching event and is the part of a settings file most worth seeing. Hooks a plugin adds are rows of that plugin ("from plugin X").
- One generic parser reads every documented JSON shape; formats that are not stable (Kiro) are read best effort and say so. Warnings: a missing script, or download-and-run in one step. Nothing is run.
## Safety
- Health has a "Safety" area: findings to review in installed skills and plugins, never "safe" or "unsafe". It is offline pattern matching; nothing is run or fetched, and a clean result proves nothing. Warnings: hidden Unicode, text that tells the agent to hide things from you, download-and-run commands, a plugin hook that runs a missing script. Information: long encoded blocks, bare-IP and paste-site links, credential file names.
- Health "Config files": your user-level settings and MCP files (JSON), with secrets in plain text as information (they are expected in a personal file) and permission settings that skip prompts as warnings.

## Change alerts
- "Changed since" also catches a skill or plugin whose files changed, a plugin version change and an MCP server whose command or address changed, using hashes only (nothing readable is saved). It is how a tool that was fine at install and later swapped shows up; it does not say the new version is bad.

## Export to AGENTS.md
- A button on the project page combines the loaded instruction files into one AGENTS.md. It always shows the text first; replacing an existing AGENTS.md needs the confirm and makes a backup. The sources stay as they are: edit them and export again.

## Usage page
- A "Usage" page in the menu after MCP servers: cost and tokens across all apps for 7 or 30 days, with the same figures and caveats as an app's Usage card (estimates unless the app logs its own cost).

## Logs
- The log viewer is read-only. Config files are editable. The text and config viewer uses AvaloniaEdit.
- Watching and tailing:
  - Only the selected app is watched, tailing its latest-modified log file.
  - Changes in the log folder update the file list (size, modified time) live.
  - Deleting logs moves them to the Recycle Bin or Trash.
- Search has a results list and jump-to-line. Search results use the log font size, and all search-row controls are the same height.
  - Words match separately, in any order, on one line (Raw) or in one step (Conversation); "quotes" match a phrase; the regex toggle uses the whole text as one pattern. In Conversation, role:user and tool:bash keep only those steps. Matches are highlighted in the results; Enter / Shift+Enter go to the next / previous one ("3 of 120"). The search runs 250 ms after the last key.
  - The Files card has a filter (words in the file name, type or folder). It lists the newest 50 files (and the open one); "Show 50 more" adds older ones, so opening the page and each change the watcher sees rebuild few rows. Clicking a file changes only the selection and the viewer: the page keeps its scroll position (so does switching Conversation/Raw or Follow).
- Each app has a log summary.
- Highlighting and rendering:
  - Session logs are readable, with syntax highlighting.
  - Skill invocations show whether the agent or the user invoked them.
  - Levels are ordered highest to lowest, with an explanation pop-up.
  - The viewer handles escapes (`\u001b`), ANSI shell colors, and JSON inside text.
- Expanding and new messages:
  - Rows show no expand arrow when the expanded content is the same as the collapsed row.
  - New incoming messages are highlighted in a brighter color that fades out over a configurable number of seconds (default 10).
  - Log detail doesn't show read time or file size.
- The "Readable" view is called "Conversation". Both Assistant and "You" messages are left-aligned. Assistant messages have a 20 px right margin and "You" messages have a 20 px left margin.

## Settings, models and data files
- Defaults ship with the app as data in `default/` (built in, read-only): `settings.json` (every setting and its default; no defaults written in C#), `models.json` (with `models.schema.json`) and `apps.json` (was `src/Core/Assets/apps.builtin.json`).
- The user folder is `~/.ai-stackprint/` on every OS (not `%APPDATA%`): one path for docs and support, next to `~/.claude` and `~/.codex`, and not synced by roaming profiles. The first start of the new version moves the files from the old folder and leaves a note there.
  - `settings.json`: only settings that differ from the default; a missing key means the default, so a changed default reaches everyone who did not change it. Unknown keys are kept on save.
  - `models.json`: only the model fields you changed, and models you added (`provider: "custom"`).
  - `apps.json`: only custom apps (Add app). They never override a built-in app: a custom id gets the `custom.` prefix, a built-in id or name is refused ("already supported"), and when a later version supports the same tool the built-in wins, with a one-time note offering to remove the custom one.
  - `projects.json`: the project folders (paths under home written as `~/…`). They are remembered across restarts. A folder that no longer exists is shown greyed with a bin icon to remove it; clicking it opens the folder picker and the chosen folder replaces the path (the project moved).
  - `state.json`: machine-only memory (window and panel sizes, last update check, dismissed update). Never exported.
  - `cache/` (downloaded models.json, scan cache, last scan), `logs/` (crash.log, perf.log), `backups/`.
- The value used is the first found: your file, then the downloaded `cache/models.json` (models only), then the built-in default; between the built-in and the downloaded models the newer `updated` date wins.
- Every file has `"version"`: the file's format version, not the app version. A new version is needed only to rename a key, change its type or meaning, or move it; adding or removing a setting is not. Old files are upgraded on load, one step at a time (v1→v2→v3), after a copy to `backups/` (`settings.v1.json`); every upgrade step is kept and tested with a real old file. A file newer than the app is read for the keys it knows and never written; a one-time notice says changes are not saved.
- Format: JSON (System.Text.Json, no extra dependency), camelCase keys, indented, `"$schema"` for editor help. Read leniently (comments, trailing commas); the app owns these files, so saving drops comments.

## Models
- `default/models.json`: context window, max output, prices (USD per 1M tokens), long-context price tiers (`above`), price multipliers the logs can show (`fast`), retirement date and successor, source links. A missing value is unknown, not free.
- Generated from LiteLLM's `model_prices_and_context_window.json` (BerriAI/litellm, MIT) by a GitHub Action: every chat model with text output from the direct providers in the schema (Anthropic, OpenAI, Gemini, xAI, …); dated duplicates with the same numbers dropped; the `gemini/` prefix removed. No list of model families to keep: new models arrive by themselves.
- The Action runs on Sundays at 12:00 UTC and on demand. If LiteLLM cannot be reached it retries after 15, 45 and 90 minutes, then fails without a PR (GitHub emails the owner). Data that fails the schema or the sanity checks (each provider present, at least 80% of the current count, size limit) fails the run; price changes over 10x are pointed out in the PR. It reads the file at one LiteLLM commit (recorded in `source.commit`) and opens or updates one PR, only when something changed.
- The app fetches `default/models.json` from this repo through api.github.com into `~/.ai-stackprint/cache/models.json`, only when the user turned on the one switch for updates and model data ("Check GitHub for updates and model data"), at most once a day, in the background, with `If-None-Match`. A failure keeps the last good copy and is shown in Settings (date of the data, last check, Check now); data over 60 days old is noted on the Usage page.
- A logged model name uses the longest key it contains (so `claude-sonnet-4-5-20250929` and Bedrock/Vertex names find `claude-sonnet-4-5`); the result is cached per name. The Usage page shows "priced as …" when the names differ and "no price" when nothing matches.
- A "Models" page lists every model with its values; any value can be overridden (marked, with Reset) and models can be added. Overrides are saved per field in `~/.ai-stackprint/models.json`.
- Costs: tokens are summed per day and model, split by what changes the price (long-context tier, fast mode, US-only processing, 5-minute or 1-hour cache), and priced when shown, so a price change needs no new read of the logs; logs without these fields count as standard. The downloaded model data is kept per user (each user consents for themselves; nothing another user can write is trusted).
- Uses: those cost estimates and web searches, context share on the project page, a session's peak context on the Logs page, Claude Code's skill-list budget, and a Health check for configs that pin a model being retired.

## Export and import
- Settings › Export writes one file with the parts you tick: Settings, Models, Apps, Projects. Settings and models export only your changes by default, or all values (defaults included) on request; apps export only custom apps; projects export your folders. `state.json`, caches and logs are never exported, and nothing secret is ever in these files.
- Import shows what will change first, copies the current files to `backups/`, and merges: every value in the file becomes an override (so an all-values import stops following newer defaults; the dialog says so). Project folders that do not exist here can be located or skipped. An export from a newer format is refused ("update AI Stackprint").
- Consents (live limits, update and model-data checks) are never imported as on: they stay off until turned on on that machine.

## Workflow
- Write changes directly to the project folder.
- Loading: a page's controls (titles, buttons, cards) are shown at once and fade in; a card whose data is not ready shows the loading icon (shown at once). The dashboard's first scan is the exception: every card would say the same, so the title and buttons show with one loading block for the whole page, and the data fades in (`Reveal.Fade`, 180 ms) when it arrives. A refresh of data already on screen keeps it and shows the small spinner.
- When a page's data arrives only the parts that were loading fade in; titles and cards already on screen stay as they are (no blink), and a placeholder is as tall as what replaces it where the size is known (the log viewer).
- The startup scan does only what the dashboard needs. The projects' checks (they walk every project folder) run after the dashboard shows: the Health tile shows the loading icon and the sidebar badge no count until they are done, and the Health page says "Checking projects…" and fills in. A project page runs its own checks when it opens, with a loading icon. In general a page that is not shown does not load or process anything.
- The first scan's loading block says what the scan is doing: short enough for a tile: "Finding apps…", "Comparing changes…", "Safety check 45/120", "Checking configs…".
- Anything that reads a file or folder when you click it (a log file, a skill or plugin file, an app's log folders) reads off the UI thread: the viewer shows the loading icon, then the content fades in; a newer click cancels an older read.
