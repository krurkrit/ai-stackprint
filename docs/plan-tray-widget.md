# Plan: tray, usage widget and limit alerts

Built: all five phases are in the code. What stays true is now in `decisions.md` › Tray, widget and alerts, `design-system.md` (tokens, Usage widget), `architecture.md` › Tray, widget and alerts and `recipes.md` › Tray, widget or alerts; read those, not this file, for a change. Delete this file once the points below are verified.

**Where the build differs from this plan:**
- The pipe is named from a hash of the user and machine names (not the SID), so no Windows-only API is needed; only this user can open it (`CurrentUserOnly`).
- macOS and Linux show our own alert pop-up (`AlertPopup`, the look of the alerts mockup) instead of `UNUserNotificationCenter` / D-Bus; those are still to do.
- Windows toasts: the WinRT API is called through COM function pointers (checked on Windows 11: the call chain shows a toast), so the app stays `net10.0`. The app id is registered under `HKCU\Software\Classes\AppUserModelId\AiStackprint.App`, not taken from Velopack's shortcut.
- The widget's opacity, Always on top and Show at login are also in Settings › Usage limits › Tray and widget, with the tray switches; the alert thresholds are in its Alerts card.
- Memory with only the tray: the window is hidden, not destroyed, so it opens again at once on the page it was left on; the working set is handed back to Windows when it hides. Measured with `--perf`: 288 MB working set before that, about 100 MB after (heap 42 MB, mostly the last scan's data); the 80 MB goal is not met yet. Dropping the scan data while hidden (and scanning again on open) is the next step if it matters.

**Still to verify by hand:** a real toast and its buttons from an installed build (the `aistackprint:` link starting a second copy); the tray on Linux (a StatusNotifierItem host) and macOS (menu on click); the login entry after a Velopack update.

Each phase is one Conventional commit (or PR), ends with `dotnet format`, a 0-warning build, all tests passing, and the docs updated.

## Goal
AI Stackprint keeps checking usage limits while its window is closed. It then:
- records every reading, so the pace chart has no gaps;
- shows a small widget with each limit;
- sends system notifications before a limit runs out, or when usage is being left unused.

## Mockups (the target look)
All three are drawn with the app's own colors, light on the left and dark on the right. The HTML source is next to each picture.

| File | Shows |
|---|---|
| `docs/mockups/tray-widget.png` | The widget at 70 % background opacity over the desktop, and the settings its gear opens |
| `docs/mockups/tray-close-open.png` | Closing the window (×): the one-time "Still running in the tray" notice; opening it again from the tray icon and its menu |
| `docs/mockups/tray-alerts.png` | The four alerts. Their content is ours; the system draws the frame |

## Decided
- **One app, one exe, one process.** `AiStackprint.exe` is the only process Task Manager shows. The exceptions are short-lived ones: Velopack's `Update.exe` while an update installs, and an app's own CLI during a plugin change.
  - The tray, the background checks, the widget and the main window all live in that process.
  - The widget reads the readings in memory.
  - No second program and no file shared between programs.
- **Opt-in.** Nothing runs in the background until the user turns on "Keep running in the tray" in Settings › Usage limits. Until then, closing the window quits, as today.
- **Colors follow the main app.** The widget's bars, the tray ring, buttons, check boxes, switches and the slider use the accent (`W.Accent`). Text, muted text, the card and the border use `W.Text`, `W.Muted`, `W.Surface` and `W.BorderKey`. A custom accent or theme in Settings › Appearance changes the widget too. The warning lines use `W.WarningKey` with a warning icon, as in the main app; the bars never change color.

## Rules that apply (add to `AGENTS.md` in phase 1)
- **Background running:**
  - Off by default. Turning it on is the user's confirmation.
  - "Start at login" writes the platform's login entry (Windows `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, macOS `~/Library/LaunchAgents/`, Linux `~/.config/autostart/`) only while that switch is on.
  - Turning the switch off, or uninstalling (Velopack hook), removes the entry.
- **Limit checks:**
  - The same rules as today: only apps in `limitsEnabled`, and at most one call per app per `limitsRefreshMinutes`.
  - Tokens stay in memory, so running longer changes nothing about how they're handled.
- **Notifications:** they go only to the local system notifier. Nothing is sent anywhere else.

## How it runs

### Start
- **`AiStackprint.exe`** (Start menu, Dock, shortcut): opens the main window. If a copy is already running, see Single instance.
- **`AiStackprint.exe --tray`** (the login entry):
  - starts with no main window: tray icon, background checks, alerts;
  - shows the widget if "Show widget" and "Show at login" are on;
  - skips the first scan until the window is first opened, so a login costs little.
- The app's lifetime becomes `ShutdownMode.OnExplicitShutdown`. Only Quit (tray menu, or "Quit now" on the notice) and the system shutting down end it.

### Single instance
- **Windows:** a named mutex `Local\AiStackprint`, plus a named pipe `AiStackprint-<user SID>`.
- **Linux:** a lock file `~/.ai-stackprint/app.lock` and a Unix socket next to it.
- **macOS:** the system already keeps an app bundle to one copy; the lock file covers `dotnet run`.

A second start sends `show` (or `show <route>`) to the first copy and exits with code 0. The first copy opens or raises its main window. The same message carries a notification's "Open" (see Alerts).

### Close and open
| Action | Result |
|---|---|
| Main window × (tray on, `closeToTray` true) | Hides the window: `MainWindow.Leave()` runs and the pages are dropped, then `MemoryTrim`. The tray, checks, widget and alerts keep running. The first time, the "Still running in the tray" notice shows. |
| Main window × (tray off, or `closeToTray` false) | Quits, as today |
| Widget × | Hides the widget (`widgetEnabled` = false). Turn it on again with "Show widget" in the tray menu. |
| Tray icon, left click (Windows, Linux) | Opens the main window, on the page it was left on |
| Tray icon, click (macOS) | Opens the menu (the macOS convention); "Open AI Stackprint" is its first item |
| Tray icon, right click | The menu |
| Click an app name in the widget | Opens the main window on that app's page |
| "Open" on a notification, or clicking the notification | Opens the main window on that app's page |
| Tray menu › Quit AI Stackprint | Saves state, removes the tray icon, exits |

## Tray

### Icon
- **The ring:**
  - It shows the most-used limit among the windows the widget shows: the highest `UsedPercent`, or all enabled windows when the widget shows none.
  - Its stroke is the accent, over a track of the text color at 12 % alpha.
  - It is drawn into a bitmap at 16, 20, 24 and 32 px (100–200 % scale) and set as the `TrayIcon.Icon`.
  - It is redrawn when a reading changes the most-used value by 1 % or more, and when the theme or the accent changes.
- **Light or dark:** the ring takes its colors from the theme the taskbar uses, which may differ from the app's.
  - Windows reads `SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`.
  - macOS uses the menu bar appearance.
  - Linux follows the app's theme.
- **No readings:** with no limits on, or none read yet, the icon is the app icon (the cat).

### Tooltip
- One line per app shown, for example `Claude Code · 5-hour 75%, weekly 66%`.
- Windows cuts tooltips at 127 characters; past that, it shows the first apps and `+N more`.

### Menu (`docs/mockups/tray-close-open.png`, panel 2)
In this order:
1. The usage line: the most-used app, for example `Claude Code · 5-hour 75%, weekly 66%`. Not clickable; muted.
2. Separator.
3. **Open AI Stackprint**, the default item. It's bold where the platform allows; Windows' native menu does not, so there it's simply first.
4. **Show widget**, a check item ("On" in the mockup is the check).
5. **Pause alerts for 1 hour**. While alerts are paused, this item reads `Resume alerts (paused until 09:42)`.
6. **Settings**: opens the main window on Settings › Usage limits.
7. Separator.
8. **Quit AI Stackprint**.

Icons as in the mockup, from `icons.json`: window, list, bell-off, gear, logout. Add any that are missing.

### "Still running in the tray" notice (`docs/mockups/tray-close-open.png`, panel 1)
- **When:** shown once, the first time the main window closes to the tray (`UiState.TrayNoticeShown`).
- **Window:** our own small borderless window (not a system notification), so it looks the same on every platform.
  - Placed at the bottom right of the work area, 10 px from the edges, above the taskbar.
  - 258 px wide; height fits the content.
  - Not shown in the taskbar.
- **Look:** the widget's card colors, `Radius.L`, a 1 px `W.BorderKey` border and 12 px padding.
- **Content:**
  - the ring icon at 14 px and "AI Stackprint" in `Caption`, muted;
  - the title "Still running in the tray" in Emphasis;
  - the text "Usage is still checked and alerts still come. Click the tray icon to open the window; Quit is in its menu." in muted;
  - the buttons **Got it** (accent) and **Quit now** (default).
- **Behavior:** it closes on Got it, or after 20 seconds without a pointer over it.

## Widget (`docs/mockups/tray-widget.png`, top)

### Window
- **Kind:** a borderless window (`SystemDecorations.None`) with a transparent background (`TransparencyLevelHint` Transparent).
  - Not shown in the taskbar.
  - `SizeToContent.Height`, 280 px wide.
  - Not resizable.
- **Position:**
  - Dragging anywhere except a button moves it (`BeginMoveDrag`).
  - Its position (and screen) is saved in `UiState.WidgetPosition`, and on start it is put back inside that screen's work area.
  - The first time it opens at the bottom right, 16 px above the taskbar.
- **Always on top:** follows `widgetOnTop`.
- **Opacity:**
  - Only the card background is see-through: `W.Surface` at `widgetOpacity` % alpha. The text, bars, icons and border stay solid.
  - With the pointer over it, the card fades to 100 % over `Duration.Fast` (150 ms) (no fade when Animations are off).
  - The range is 30–100 %, in steps of 5.

### Layout (sizes in px for the 14 px base, scaled by `W.Size` like the rest of the app)
- **Card:** `W.Surface` at the chosen opacity, a 1 px `W.BorderKey` border, `Radius.L` corners and padding 10 top, 12 sides, 12 bottom.
- **Header row:**
  - left: `Usage limits · now HH:mm` in `Caption`, muted (the time of the newest reading);
  - right: a gear and an ×, `IconSize.XS`, muted, 8 apart, each a 24 px button with the hover tint `W.Subtle`;
  - then 6 below.
- **App block:** one per app with at least one window shown, in the order of `limitsEnabled`.
  - Its title row: the app name in Emphasis `Small`, and the plan (`LimitSnapshot.Plan`) on the right in `Caption`, muted.
  - Blocks after the first have a 1 px `W.BorderKey` line above, with 10 above and 10 below it.
- **Limit row:** one per window shown, in the provider's order, 8 below the row before.
  - A 3-column grid: 52 px for the window name (`Small`, muted), the bar, and 34 px for the percent (Emphasis `Small`, right-aligned, digits aligned with `tnum`). The columns are 8 apart.
- **Bar:**
  - 6 tall (a new `Sizes.BarWidget`; the Usage card meter's `Sizes.BarThin` is 8, too heavy at this size), with radius 3 (the bar is round at its ends).
  - Track `W.Subtle`; fill `W.Accent`, as wide as `UsedPercent`.
  - **Pace mark:** a 2 px line in `W.Text` at 55 % opacity, at the even pace of that window: elapsed ÷ length × 100, with the length from `LimitPacing.Length` and the start = reset − length. It runs 3 px above and below the bar.
  - No mark when the length is unknown.
- **Line under a row:**
  - 3 below the row, indented 60 px to line up with the bar, in `Caption`.
  - Muted, or `W.WarningKey` with a warning icon (`IconSize.XS`, 3 px gap) when it warns.
  - The texts, using the same `LimitPacing` numbers as the pace chart:

| Window | Case | Text | Warns |
|---|---|---|---|
| Short (5-hour) | `ShortPace.FullAt` before its reset | `Full about HH:mm · resets HH:mm` | yes |
| Short | otherwise | `Resets HH:mm` | no |
| Long (weekly) | `LimitPace.FullAt` before the reset | `Limit ddd HH:mm · resets ddd HH:mm` | yes |
| Long | `LeftAtReset` > 10 | `N% left at the reset · ddd HH:mm` | no |
| Long | otherwise | `Lasts to the reset · ddd HH:mm` | no |
| Any other window (no known length) | | `Resets ddd HH:mm`, or nothing when there is no reset | no |

- **When readings fail:** while a limit isn't read (sign-in expired, offline, rate limited), the block keeps the last reading. Its title row then reads `as of HH:mm` in muted, and the line under it shows the status message from `LimitsView` (muted, no warning icon).
- **Empty widget:** with nothing to show, the card has one muted line, "Turn on live limits in Settings › Usage limits", and a link that opens that page.

### Settings pop-up (`docs/mockups/tray-widget.png`, bottom)
- **How it opens:** the gear opens it under itself, like the other pop-ups (`InfoFlyout` look): `W.Surface`, a 1 px `W.BorderKey` border, `Radius.L` and 12 px padding, 280 px wide. Changes apply at once and are saved.
- **"Widget settings":** Emphasis, then:
- **"Show"** (`Caption`, muted label): one check box per app in `limitsEnabled` that has a limit provider.
  - Each app has the list of its windows on the right in `Caption`, muted (for example `5-hour, Weekly`).
  - Under each app, indented 22 px, a check box per window it reports (for example `5-hour`, `Weekly`, `Weekly · Sonnet`).
  - Unticking an app hides its block. Unticking every window of an app unticks the app.
  - Apps not read yet list their windows after the first reading.
- **"Background opacity":** a slider from 30 to 100 in steps of 5, with its value right-aligned (`70%`). Under it, in `Caption` muted: "Text stays solid; under the pointer the widget is opaque."
- **Switches** (`ToggleSwitch`, label on the left): **Always on top** (`widgetOnTop`) and **Show at login** (`widgetAtLogin`).

The same settings sit in Settings › Usage limits, in a new "Tray and widget" card.

## Alerts (`docs/mockups/tray-alerts.png`)
These are system notifications. The system draws the frame (app icon, name, time) in its own style; we supply the title, text and buttons as below. Each kind has its own switch, and its threshold can be changed in Settings › Usage limits › Alerts.

| Kind | Fires when | Title | Text | Buttons |
|---|---|---|---|---|
| Short window nearly full | `UsedPercent` crosses 80, then 95 (`alertShortAt`), upward, within one window | `{App}: {window} limit at {N}%` | `Full about HH:mm at this pace. It resets at HH:mm.`, or `It resets at HH:mm.` when it won't fill | Open, Pause alerts for 1 hour |
| Long window runs out early | `LimitPace.FullAt` is more than 12 h (`alertEarlyHours`) before the reset | `{App}: weekly limit runs out early` | `At this pace it's reached ddd HH:mm, {span} before the reset. Slow to X% a day to last.` | Open |
| Long window reset | Its `ResetsAt` moves on to a new window | `{App}: weekly limit reset` | `You used N% last week. Next reset ddd HH:mm.` (N = the last reading of the old window) | — |
| Usage left | 24 h (`alertLeftHours`) before the long window's reset, with `LeftAtReset` over 25 % (`alertLeftPercent`) | `{App}: N% left before the reset` | `It resets ddd HH:mm. You can use about X% a day.` | Open |

**Window names:**
- "weekly" stands for the long window's name in lower case.
- The short window's name is shown as the provider gives it (`5-hour`).

**Firing at most once:**
- Each alert fires at most once per key, saved in `UiState.AlertsSent`:
  - app + window + `ResetsAt` (+ threshold) for the short window, the reset and usage left;
  - app + window + date for running out early, so at most once a day.
- Keys older than 8 days are dropped.
- The reset alert is on by default for long windows only. Short-window resets are off by default (one every 5 hours is noise).

**Pausing and settings:**
- "Pause alerts for 1 hour" sets `UiState.AlertsPausedUntil`. Alerts that would have fired in that time are skipped, not queued.
- The system's Do Not Disturb / Focus applies on top.
- Alerts fire only while the tray is on, or the main window is open.
- Numbers are whole percents; `X% a day` has one decimal, as in the pace chart.

**Per platform (to verify in phase 4; the main risk):**
| Platform | How | Buttons | Click |
|---|---|---|---|
| Windows 10/11 | Toast notification (WinRT `ToastNotificationManager`), using the app user model id on the Start menu shortcut that Velopack creates | Yes | Activates the app with arguments: sent through the single-instance pipe |
| macOS | `UNUserNotificationCenter` (needs the signed app bundle; see `install-macos.md`) | Yes (categories) | Delegate callback |
| Linux | `org.freedesktop.Notifications` over D-Bus (`Tmds.DBus.Protocol`, already pulled in by Avalonia) | Where the server supports actions | `ActionInvoked` signal |

Avalonia has no notification API, so each platform gets a small class behind one interface (`INotifier`) in `src/App`. Tests use a fake.

## Settings and state
**New keys in `default/settings.json`** (with comments, under "Usage limits"):

| Key | Default | Meaning |
|---|---|---|
| `trayEnabled` | `false` | Keep running in the tray: checks, widget and alerts with the window closed |
| `closeToTray` | `true` | With the tray on: × on the main window hides it instead of quitting |
| `startAtLogin` | `false` | Start with `--tray` at login (writes the platform's login entry) |
| `widgetEnabled` | `false` | Show the widget |
| `widgetAtLogin` | `true` | Show the widget when started at login |
| `widgetOnTop` | `true` | The widget stays above other windows |
| `widgetOpacity` | `100` | Card background opacity, 30–100 |
| `widgetShow` | `{}` | App id → window names shown; an app missing means all its windows |
| `alertsEnabled` | `true` | Send alerts (needs `trayEnabled` or an open window) |
| `alertShortAt` | `[80, 95]` | Short-window percents that alert; `[]` turns this alert off |
| `alertEarlyHours` | `12` | "Runs out early" when the limit comes this many hours or more before the reset; `0` = off |
| `alertReset` | `true` | Alert when a long window resets |
| `alertLeftHours` | `24` | Hours before the reset for "usage left"; `0` = off |
| `alertLeftPercent` | `25` | …when more than this percent would be left |

**New in `UiState` (`state.json`, this machine only):**
- `WidgetPosition` (x, y and the screen's bounds);
- `TrayNoticeShown`;
- `AlertsSent` (key → time);
- `AlertsPausedUntil`.

## Recording
- **The timer:** `MainWindow.Nav.Limits()` (a 20-second `DispatcherTimer` that asks `LimitsService` for every installed app with limits) moves to an app-level `LimitsTimer` started by `AiStackprintApp`. It then runs with or without the window. `MainWindow` subscribes to `LimitsService.Updated` as it does now.
- **The store:** `LimitHistory` moves from one rewritten `cache/limit-history.json` to files that are only appended to: `cache/limits/YYYY-MM.jsonl`, one reading per line.
  - Each line is `{at, app, windows[]}`.
  - Reading loads the last 8 days for the pace chart.
  - Files older than 90 days are deleted.
  - The old file is read once and moved (before 1.0 no format version is needed).
- **Alerts:** the alert checks run in `LimitsService.Updated`, on the UI thread.

## Look in the code
- **New UI files** (each under about 250 lines):
  - `TrayHost` (icon, menu, tooltip);
  - `TrayIconArt` (the ring bitmap);
  - `TrayNotice`;
  - `UsageWidget` (+ `.Rows`, `.Settings`);
  - `LimitsTimer`;
  - `SingleInstance`;
  - `Notifier` (+ `.Windows`, `.Mac`, `.Linux`);
  - `LoginItem` (per platform).
- **New Core services:**
  - `LimitAlerts`: from the readings, the settings and the sent keys, decides which alerts to send. Pure, fully tested.
  - `WidgetRows`: the rows and texts of the widget from the snapshots and `LimitPacing`. Pure, tested.
- **Tokens:** only existing ones (`Space`, `Radius`, `TextSize`, `IconSize`, `Sizes`, `Duration`). The widget's 280 px width and 52/34 px columns go into `Widths` as `Widget`, `WidgetLabel` and `WidgetPercent`; the 6 px bar into `Sizes.BarWidget`. No literal sizes in views.

## Phases
1. **Tray and process.**
   - Single instance, `--tray`, `OnExplicitShutdown`, close to tray, the tray icon (app icon first), the menu, and the first-close notice.
   - `LimitsTimer` moves to the app.
   - The settings `trayEnabled`, `closeToTray` and `startAtLogin` with their login entries (and removal on uninstall).
   - The `AGENTS.md` rules.
2. **Recording.** The JSONL store, the move of the old file, 90-day retention, and the ring icon with its tooltip.
3. **Widget.** The window, the rows and texts (`WidgetRows`), opacity and hover, dragging and position, the settings pop-up, and the "Tray and widget" card in Settings.
4. **Alerts.** `LimitAlerts`, the notifiers per platform, Open/Pause handling through the single-instance pipe, and the Alerts settings.
5. **Polish.**
   - `--perf` reports the memory used while running only in the tray (goal: under 80 MB working set with the window closed).
   - Screenshots of the widget and the notice join `build/test-and-capture.ps1`.
   - Compare against the mockups in both themes.

## Tests
- **Core:**
  - `LimitAlerts`: every kind, the thresholds crossed only upward, sent at most once, keys dropped after 8 days, pause, switches off.
  - `WidgetRows`: every text case in the table, the pace mark position, the stale block.
  - The JSONL store: append, read the last 8 days, retention, the move from the old file.
- **UI, headless:**
  - the widget in both themes: rows, warning lines, opacity, `as of` when stale;
  - the settings pop-up writes `widgetShow`, `widgetOpacity` and the switches;
  - the tray menu items and their actions;
  - closing the main window hides it when `closeToTray` is on, and quits when it's off;
  - the notice shows once.
  - Screenshots `feature-tray-widget-{light,dark}.png` to compare with `docs/mockups/tray-widget.png`.
- **Seams:** the login entry and the notifier go through interfaces with fakes, so tests never touch the registry, LaunchAgents, autostart or the real notifier (`docs/testing.md`).

## To verify before building
- **Avalonia `TrayIcon` on Linux:**
  - It needs a StatusNotifierItem host: KDE has one, GNOME needs the AppIndicator extension.
  - Without a host, closing should quit, as with the tray off. Detect it if Avalonia allows, or say so in the setting's help.
- **Windows toasts from an unpackaged app:**
  - Can the App project call the WinRT API without changing its target framework (`net10.0`)?
  - If it can't, decide between multi-targeting `net10.0-windows10.0.19041` and an alternative.
- **The login entry path:** Velopack's stable entry (the exe at the install root) so it survives updates.
- **macOS:** notification permission on the first alert, and whether the unsigned `dotnet run` build can show notifications at all.
