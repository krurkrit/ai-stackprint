# Product and UI decisions

Decisions agreed with the owner. Read before UI work. Rules in `AGENTS.md` win on conflict.

## General
- Name is "AI Stackprint" everywhere, including the Task Manager process name, with the app icon on the process group. Never "AiStackprint" in UI, and no "Owl" in the name.
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
- The "Installed" status is plain green text (`#6CCB5F` in the dark theme, `#0F7B0F` in the light theme), no pill and no border.

## Dashboard and Apps
- Every card header is the same: icon on a tinted square, then the title in the heading weight (`W.Section`). Stat tiles use it too.
- Dashboard shows summary charts of plugin and skill counts by app and by author, and what plugins add (skills, commands, agents, hooks, MCP and LSP servers, each out of all plugins) with their top keywords; a bar or keyword opens the Plugins list searched for it (has:mcp, keyword:"x"). Don't add an "AI apps and orchestrators" list there; the Apps page already has it.
- Rescan is one icon button in the title bar, before the theme switch (also F5 and Ctrl+K; a spinner shows while it runs and a toast says what it found). It is not in the left menu or on the Dashboard, and neither is "Add project folder" (that is in the left menu).
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
- Switching between Conversation (or Lines) and Raw keeps your place: the step in the middle of one view is put in the middle of the other (steps know their file line; not for files shown cut to their end, where the last line is shown instead).
- Raw view: the last line scrolls to the middle of the view (empty space below it), so the newest line is never at the bottom edge.
- "All files" (on by default when the app has more than one log file) makes the search look in every log file of the app, newest first (at most 200 files, 20 matches per file, 1,000 in all). Results are lines of the files, grouped by file with the update time; a click opens that file and goes to the line in the view you are in (the step that holds it in Conversation and Compact, the row in Lines, the line in Raw; a file shown cut to its end has no reliable lines for steps, so it opens in Raw). Off: only the open file is searched. The x on the results closes them and keeps the search words.
- Search has a results list and jump-to-line. Search results use the log font size, and all search-row controls are the same height.
  - Words match separately, in any order, on one line (Raw) or in one step (Conversation); "quotes" match a phrase; the regex toggle uses the whole text as one pattern. In Conversation, role:user and tool:bash keep only those steps. Matches are highlighted in the results; Enter / Shift+Enter go to the next / previous one ("3 of 120"). The search runs 250 ms after the last key.
  - Opening an app's logs shows its most recently updated file. The Files card has no name filter (people do not know file names): rows are named by their update time, with the type and file name below. It lists the newest 50 files (and the open one); "Show 50 more" adds older ones, so opening the page and each change the watcher sees rebuild few rows. Clicking a file changes only the selection and the viewer: the page keeps its scroll position (so does switching Conversation/Raw or Follow).
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
- Text logs (`"format": "text-log"` on an app's log source: Cursor, Windsurf, Kiro, Claude desktop, Goose, GitHub Copilot, Warp, OpenCode, Antigravity, Orca) open in a "Lines" view: one compact row per log line (time, level, message), the lines that follow a message (a stack trace) under it, colored by level, up to the last 5,000 lines. The format of each line (`[time] [level] message`, key=value, JSON, glog, Orca trace spans and daemon events) is found from the file's first lines, so a folder that mixes formats still reads. `role:error` / `role:warn` in the search keep only those levels. Raw is one click away. Only sources whose real files were checked on a machine have the format; other apps stay Raw until someone checks their logs.
- Errors are listed in the Bookmarks popup (the toolbar bookmark button), above the bookmarks and notes: a click goes to the step. There is no Next error button or F8; the red dot in the graph and the level chips mark errors too. A text log shows only the errors. Session logs: a tool result the app marked as failed (Claude's `is_error`); Codex has no such mark.
- Conversation shows what each reply cost: under the first step of a reply, "model · 92K in (90K cached) · 500 out · $0.0412" (tokens of the request and of the reply, and the estimate at list price: the same figures as the Usage page; a streamed reply counts once), and the header adds "≈ $1.23 estimated" for the session. Only Claude sessions record tokens per reply; other formats show none. Cost is omitted when the model has no price.
- Conversation also has: an edit or write call shown as a diff (old lines with -, new with +); a "N files changed" chip (hover for the list); a Copy button on each step (the step as Markdown, shown while the pointer is over it); and an Export button that writes the whole session to a Markdown or HTML file chosen in a save dialog (the only file the viewer writes; the log is never changed). Codex replies show tokens and cost like Claude's (a repeated token count is ignored).
- A session has three views, switched by three buttons next to the search: Conversation (cards; the default), Compact and Raw (the file as written). A text log has Lines and Raw. Switching keeps your place. Compact shows one line per step (time, who, the text cut to the line, the cost); a click opens the step's full card in a popup under the line: rendered Markdown, highlighted code, tool input and output opened, its tokens and cost, and a Copy button (the step as Markdown). The Lines view of text logs keeps its own rows.
- A conversation with sub-agents has a branch graph on the right of the steps, like `git log --graph`: the main session is a line; each agent branches off where it was started and rejoins where the main session picks up its result; agents that run at the same time get lanes side by side and a lane is used again after its agent has rejoined. A step has its dot only in its own lane; other open lanes show just the line. Hovering a step's dot area names its agent. The chips above the steps (Main, Agent 1 …) hide or show that agent's steps.
- Errors in sessions: a tool result the app marked as failed (Claude's `is_error`) shows as "Failed · tool" in red (a red bar on the card), is counted in the header ("2 failed results"); they are listed in the Bookmarks popup. Only explicit marks count: no guessing from the text of the result. Codex logs carry no such mark, so Codex sessions show none.
- The graph (sub-agent branches) has a column of its own at the right of the steps, so it covers no text or button: the side menu's background at 50%, a thin divider on its left, clicks pass through it, and a session with only the main agent has no graph at all. Dots as in the session-graph mockup: radius 4 for every step (5 for your prompts and errors) with a 1.5 ring in the surface color, 22 px lanes, and an S curve that leaves and arrives straight down. The graph is the right column of a session (no separate overview strip): every step has its dot in its own row, on the main line; gray for most steps, blue for you, red and larger for an error, a ring in the accent for a bookmarked step, and a sub-agent's own color on its branch. A session without sub-agents has just the main line and no agent chips. Text logs (Lines) have no graph.
- Links from a step: a Compact line's popup starts with buttons for what the step points at: its model (Models page), a Skill call's skill (Skills page, filtered), an MCP tool's server (Servers page) and the file an Edit, Write or Read touched (opens in its program; only when the path is absolute and exists). They show also for a file shown cut to its end.
- Bookmarks and notes: in both Conversation and Compact a step has a bookmark button (shown on hover, always shown once the step has a bookmark or a note): a click bookmarks the step and a second click takes the bookmark off (a note stays). The step's popup (click a Compact line) has the same Bookmark toggle and the note; every control follows a change made in another (`StepContext.Watch`). A bookmarked step shows a filled bookmark, one with only a note a note icon (the note on hover). The Bookmarks button next to Export lists the file's bookmarks; a click goes to the step. They are kept in `state.json` by file and line (machine-only, never exported, at most 500; the oldest go first). A long file shown a window at a time keeps its bookmarks: every step has the file's own line. A file of a format not read by lines (cut to its last 8 MB) has none. Not for text logs (Lines).
- The chips that hide or show something (agents, log levels) are dimmed when off, not filled.
- The Lines view has a chip per level with its count ("Error 3"): click to hide or show those lines.
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
- The Models page has two tabs: Catalog (list, search, edit, add; no tick column) and Compare (four type-ahead boxes pick 2 to 4 models; facts, radar, benchmarks).
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
- A long session opens with only its last 20 cards (60 lines in Compact and text logs: lighter rows); scrolling up within about 600 px of the top builds the next ones above without moving the view. "Show earlier steps" and a search hit still build on demand. Cards cost about 8 ms each to lay out, which is why the first batch is small.
- Claude Code keeps each sub-agent in its own file (`<session>/subagents/agent-<id>.jsonl`; the main file has only the call and its result). The viewer reads them with the session (`SessionAgents`, up to 40 files, 4 MB each) and puts their steps among the session's by time, so the graph shows the branches. Their steps have a place of their own, the agent file (`LogEntry.Source`) and its line, so they can be searched, bookmarked and reached like any step; Raw shows only the main file. When a long session is shown a window at a time, agents that ran before the window are not shown; the header says "N sub-agents earlier". Sessions without a `subagents` folder are unchanged. Totals in the Logs summary and the Usage page still count the main file only.
- A long session opens on its last 5 MB, so older sub-agents are only counted until you scroll to them. The summary has a "N sub-agents earlier: show" button: it reads the whole file (off the UI thread, about a second for 66 MB) and shows the stretch around the newest agent that started before what is shown (and the agents running then), a dozen main steps either side, scrolled to the fork, with the file's own line numbers (so bookmarks work). Pressing it again goes one agent further back; "Back to the end" returns. Follow and refresh leave a window alone (`SessionAgents.ReadAround`).
- A long log is read a window at a time (LogWindow, 5 MB; Raw 256 KB) instead of only its end: it opens on the end, and scrolling into the first or last quarter reads the window a quarter (1.25 MB) further on, keeping the step at the top in place; buttons at the edges do the same. Only the steps near the view are built. A part read above the window (256 KB) pairs a tool result with its call; the totals count the window. The status says where you are ("a–b MB of c MB"). A text log split into numbered or dated files (LogParts: same folder, name without its digits) is read as one log, oldest first, so scrolling up crosses into the file before; sessions never are. While a window not at the end is shown, new lines only show the "New lines" button.
- A search result goes to its place in the view you are in (Conversation, Compact, Raw), in the open file or another one, and is tinted like a new line and fades (Raw: the line is selected and tinted). The log shows the loading icon at once and until it is there. When the steps shown do not hold the result (the result is outside the window shown) the 5 MB window around its line is read (`LogWindow.Around`) and shown with "Back to the end"; the Files list scrolls to the file. The fade time is 1 to 60 seconds. Clicking a result sends its file and line to the view; opening the file or reading the part around the result does not search again, so the results stay as they are.
- Every step has a place: its file (`LogEntry.Source`, null = the session file; a sub-agent step has its agent file) and its line. A search result, a bookmark or an error is such a place, and `StepIndex` (an `ILookup` by file and line plus a sorted line list) finds the step: the last step that starts at or before the line, all steps that start on it. Searching in Raw and in all files reads the file; searching in Conversation and Compact reads the steps' text of the whole session file and its sub-agents' files (`SessionSearch`: parsed in the background, only the results kept, about 0.8 s for a 66 MB session; a raw search would match the JSON keys of every line). When the steps shown are the whole session, they are searched in memory. A result in a sub-agent that is not in the part shown loads the part of the session around that agent (`SessionAgents.ReadAroundAgent`). The "all files" search also reads a session's sub-agent files, and its results say "Sub-agent · Line N".
- Follow never reads or parses on the UI thread, and reads only what is new: a session keeps its parse between pieces (`SessionTail`) and its new steps are added at the end of the view, so the steps already shown keep their controls, scroll and tints. The search results are not searched again on a change: the matches in the new steps (Raw: the new bytes) are added to them. A file that shrank or was replaced, or an earlier step that changed, is read again from the start.
