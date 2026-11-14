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
- Appearance settings: fonts, sizes, weights, log font and size, log line numbers (hidden by default), and accent, hover and selection colors per theme. There is no Theme row; the theme switch lives in the side menu.
- UI and log font sizes both use a typed number input, not a dropdown. Log size defaults to 14, range 10–18.
- Body and Emphasis weights share one list.
- Inputs (text, number and search boxes, drop-downs, check boxes, radio buttons) share one height, and one-line text sits in the middle. Multi-line inputs start their first line where a one-line input shows its text and leave the same gap below the last line. The gaps come from the font's line height (`Theme.Inputs`), so this holds at any font and size; do not set a fixed `MinHeight` or padding on an input.
- Dark-theme status is a green outline with normal foreground text, not a filled badge.

## Dashboard and Apps
- Every card header is the same: icon on a tinted square, then the title in the heading weight (`W.Section`). Stat tiles use it too.
- Dashboard shows summary charts of plugin and skill counts by app and by author, and what plugins add (skills, commands, agents, hooks, MCP and LSP servers, each out of all plugins) with their top keywords; a bar or keyword opens the Plugins list searched for it (has:mcp, keyword:"x"). Don't add an "AI apps and orchestrators" list there; the Apps page already has it.
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

## MCP servers
- One page lists every MCP server in every app's config files, at every level, highest first: what runs or where it connects, and notes (not pinned, key in plain text, env names, disabled). Warnings alone on request; a search narrows it.
- Read-only and offline: servers are never started or asked for their tools. Secret values are never shown.

## Safety
- Health has a "Safety" area: findings to review in installed skills and plugins, never "safe" or "unsafe". It is offline pattern matching; nothing is run or fetched, and a clean result proves nothing. Warnings: hidden Unicode, text that tells the agent to hide things from you, download-and-run commands, a plugin hook that runs a missing script. Information: long encoded blocks, bare-IP and paste-site links, credential file names.
- Health "Config files": your user-level settings and MCP files (JSON), with secrets in plain text as information (they are expected in a personal file) and permission settings that skip prompts as warnings.

## Logs
- The log viewer is read-only. Config files are editable. The text and config viewer uses AvaloniaEdit.
- Watching and tailing:
  - Only the selected app is watched, tailing its latest-modified log file.
  - Changes in the log folder update the file list (size, modified time) live.
  - Deleting logs moves them to the Recycle Bin or Trash.
- Search has a results list and jump-to-line. Search results use the log font size, and all search-row controls are the same height.
  - Words match separately, in any order, on one line (Raw) or in one step (Conversation); "quotes" match a phrase; the regex toggle uses the whole text as one pattern. In Conversation, role:user and tool:bash keep only those steps. Matches are highlighted in the results; Enter / Shift+Enter go to the next / previous one ("3 of 120"). The search runs 250 ms after the last key.
  - The Files card has a filter (words in the file name, type or folder).
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

## Workflow
- Write changes directly to the project folder.
