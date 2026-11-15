# Plan: data files, models and export

Implements "Settings, models and data files", "Models" and "Export and import" in `decisions.md`. Delete this file when the last phase is merged.

Each phase is one Conventional commit (or PR), ends with `dotnet format`, a 0-warning build, all tests passing, and the docs updated (file map and sections in `architecture.md`, `AGENTS.md` rules, `adding-apps.md`). Phases that touch loading or the Usage page are measured with `--perf` (published build) before and after.

## Decided
1. Phase 4: a project folder that no longer exists is shown greyed with a bin icon; clicking it opens the folder picker and the chosen folder replaces the old path (the project was moved).
2. Phase 7: one switch for updates and model data ("Check GitHub for updates and model data"). The download is saved per user to `~/.ai-stackprint/cache/models.json` (consent is per user, no admin rights, and nothing another user can write is trusted), apart from your overrides in `~/.ai-stackprint/models.json`.
3. Phase 9: the logs carry what the costs need (checked on real logs, field names only):
   - Claude Code, per reply (`message.usage`): `input_tokens` + `cache_read_input_tokens` + `cache_creation_input_tokens` (the request's input size, for tiers), `cache_creation.ephemeral_5m_input_tokens` / `ephemeral_1h_input_tokens` (which cache), `speed` (`standard` / `fast`), `inference_geo` (US-only processing), `service_tier`, `server_tool_use.web_search_requests`.
   - Codex, per `token_count` event: `last_token_usage` (input, cached input, cache write, output, reasoning) for the 272K tier, and `model_context_window` (the window, straight from the log).

## Phase 1 — Price lookup cached per model name (done)
- `Pricing.For` scans every key, builds a LINQ chain and checks the file date on each call; `UsageStats` calls it per event. Cache the result per logged name (`Dictionary<string, ModelPrice?>`, exact match first), cleared when the price data changes.
- Tests: same costs as before; a repeated name does not scan again; 5,000-entry table gives the same result as a small one.

## Phase 2 — User folder `~/.ai-stackprint` (done: files are copied one by one and never over an existing file, so a crash.log written before the move cannot block it; removed skills go to `backups/skills/`, config backups to `backups/configs/`)
- One `DataFolder` (replaces `AppCatalog.Folder`) with `cache/`, `logs/`, `backups/`. Move scan-cache.json and last-scan.json to `cache/`, crash.log, crash.seen, perf.log and perf-report.txt to `logs/`.
- First start: if `~/.ai-stackprint` does not exist and `%APPDATA%/AiStackprint` (or the macOS/Linux equivalent) does, move the files, leave `MOVED.txt` there. A failed move copies nothing half-way: it falls back to reading the old folder and says so once.
- Tests (temp home): move, nothing to move, a partly present new folder, read-only old folder.
- Docs: every path in `AGENTS.md`, `architecture.md`, `releasing.md` (perf.log next to settings.json → `logs/`).

## Phase 3 — Versioned files, defaults as data, only changes saved (done; moving window values to state.json needs no new version: an older app reading the new file just uses its defaults for them)
- `VersionedJson` (Core): reads a `JsonObject`, takes `version` (missing = 1), runs the upgrade steps in order, refuses to write a file newer than the app (read-only with a one-time notice), keeps unknown keys, copies the old file to `backups/` before an upgrade, writes atomically (temp file, then rename). Lenient read: comments and trailing commas.
- `default/settings.json` (embedded in Core) holds every setting and its default; `Settings` property initializers go. Load = defaults, then the user file over them. Save writes only keys that differ from the default (plus `version`).
- `state.json`: `PanelHeight`, `UpdateCheckedUtc`, `UpdateDismissed` move out of `Settings` (moved from an old settings.json on load).
- Tests: a default is not written, a change is; a changed default reaches a file without the key; unknown keys survive; a fake v2 with an upgrade step; a newer file is not written; backup before upgrade; atomic write; old settings.json with state keys.

## Phase 4 — Projects remembered (done; the dashboard has no project list, so the greyed row and bin are in the menu; a missing project reached from search or Back shows "Folder not found" with Choose new location and Remove; relocating onto a folder already listed removes the old row)
- `projects.json`: list of `{ path, added }`, paths under home as `~/…`. Saved on add and remove; loaded on start.
- Missing folders: greyed in the menu and on the dashboard, with a bin icon to remove; a click on the row opens the folder picker, and the chosen folder replaces the path (same position, same added date).
- Tests: save/load, `~` paths on each OS form, duplicates, missing folder.

## Phase 5 — Built-in apps in `default/`, custom apps only (done; the user file is version 2, the old bare list is version 1 and its custom ids get the prefix; "Now built in" on the Apps page lists unused entries with Remove until they are removed, rather than a one-time toast)
- Move `src/Core/Assets/apps.builtin.json` to `default/apps.json` (still embedded). Update `AGENTS.md`, `adding-apps.md`, the file map.
- `apps.json` (user): custom apps only, ids `custom.<name>`; Add app refuses a built-in id or name. Remove the "reset an overridden built-in" path. A custom app whose tool later becomes built-in: the built-in wins, one note with Remove.
- Wrap the file as `{ "version": 1, "apps": [...] }`; read the old bare list.
- Tests: prefix, refusal, built-in wins, old format read.

## Phase 6 — `default/models.json` and the weekly Action (done: 252 models from LiteLLM `21215ce`, 85 KB. Differences from the plan: the provider prefix is removed for every kept provider, not only `gemini/` (`xai/grok-5` → `grok-5`, as logged); `multipliers.us` also comes from OpenAI's regional uplift; checks are written in the generator, not a JSON Schema validator; an unknown cache price counts at the input price until phase 9; "Edit prices" adds `{ "provider": "custom", "price": {} }` per unpriced model to your models.json. The repository must allow Actions to create pull requests.)
- `default/models.schema.json` as agreed (provider list includes `custom` for user files only).
- Generator: a small C# console in `tools/ModelData` (tested like Core): reads LiteLLM's file, keeps chat models with text output from the schema's providers, removes `gemini/`, drops dated duplicates with equal numbers, converts to USD per 1M, maps fields (price, `above` tiers, `cacheWrite1h`, `multipliers.fast` and `.us`, `webSearch` (USD per search), `retires`, `successor`, sources), sorts keys, writes a stable layout. Checks: schema, each provider present, at least 80% of the current count, at most 500 KB; lists price changes over 10x.
- `.github/workflows/models.yml`: cron `0 12 * * 0` and `workflow_dispatch`; read LiteLLM's latest commit for the file, download at that commit; retries after 15, 45 and 90 minutes; on change, open or update one PR from `bot/models-update` (`contents: write`, `pull-requests: write`).
- The app: `ModelCatalog` replaces `Pricing`: built-in `default/models.json`, then the downloaded copy (newer `updated` wins), then `~/.ai-stackprint/models.json` overrides per field; longest-key lookup with the per-name cache from phase 1. `prices.json` goes: its non-zero entries that differ from the old defaults become overrides; zero placeholders are dropped. "Edit prices" links to the Models page (phase 8); until then it opens the overrides file.
- Tests: generator on a trimmed LiteLLM sample (filtering, prefix, duplicates, units, tiers, checks); catalog layering and lookup; prices.json migration.

## Phase 7 — Model data download in the app
- `ModelDataUpdate` (Core): only when the update switch is on (renamed "Check GitHub for updates and model data") or on Check now; at most once a day; GET `api.github.com/repos/krurkrit/ai-stackprint/contents/default/models.json` (raw), `If-None-Match`, 10 s timeout; validate (version, schema basics, not empty) before saving to `cache/models.json`; keep the last good copy on any failure.
- Settings shows the data date, last check and its result, Check now (with `Busy`). Usage page notes data over 60 days old.
- `AGENTS.md` rule next to the update check: off until turned on, once a day, only api.github.com, nothing about the user sent.
- Tests: fake HTTP handler for 200, 304, 403, timeout, invalid JSON, newer version.

## Phase 8 — Models page
- Menu item and route `Models` (Ctrl+K too), icon from Fluent UI System Icons. Table: Name, Provider, Context, Max output, Input, Output, Cache read, Cache write, Retires; search and provider filter. Overridden values marked with Reset; Edit opens a dialog for one model; Add model. Saves through the overrides file (only changed fields).
- The Usage page's "no price" note and "priced as …" link here with the model selected.
- Headless tests: list, override and reset, add model, search; design tokens.

## Phase 9 — Uses of model data
- Health check: config files that pin a retiring model ("… retires 2026-11-30; use claude-sonnet-5-5"), with the source link. Reads only model names, never other values.
- Costs: tokens are still summed per day and model, but split by every attribute that changes the price: long-context tier (from the model's thresholds when the log is read), speed (standard / fast), US-only processing, and cache kind (5-minute / 1-hour). Prices are applied when shown, so an override or a new download takes effect without reading logs again; the usage cache key includes the tier thresholds, so a changed threshold re-reads only that model's logs. Logs without these fields land in the default group (standard, global, 5-minute, below every tier), which equals today's estimate. Web searches are counted per day and model. What each attribute prices:
  - 1-hour cache writes at `cacheWrite1h` (Claude `ephemeral_1h_input_tokens`).
  - Long-context tiers: a reply whose input (input + cache read + cache write) is over a tier's `tokens` is priced at that tier (Codex `last_token_usage`, Claude usage).
  - Fast mode ×`multipliers.fast` when Claude `speed` is `fast`; US-only processing ×`multipliers.us` when `inference_geo` is US-only (so the generator keeps `us` too).
  - Web searches at the model's per-search price (`server_tool_use.web_search_requests`), shown as its own line.
  - First check that each Codex `token_count` event is one model reply (compare counts on real logs) before applying tiers to it.
- Context: project page share of the context window; Logs page peak context per session (Codex logs its own `model_context_window`; Claude uses `models.json`); Claude Code skill-list budget from the window size instead of the fallback.
- Each is its own commit with tests.

## Phase 10 — Export and import
- Settings › Export: tick Settings, Models, Apps, Projects; "Only my changes" (default) or "All values". One file `ai-stackprint-export.json` with `version` and a section per part; no state, caches, logs or secrets.
- Settings › Import: preview of what changes, backup to `backups/`, merge (every value becomes an override; the all-values case is explained), locate or skip missing project folders, refuse a newer format. Consents are never imported as on.
- Tests: round trip of each part, both modes, merge, newer format refused, consents stay off; headless preview dialog.

## Not in this plan
- Cloud provider prices (Bedrock, Vertex, Azure): users add them on the Models page for now.
- Per-project settings in `projects.json`.
