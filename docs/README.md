# Docs index

Find the doc for the task here, then read only the section you need: search its headings (`^## `) or the class name, never read a large doc whole. Sizes are given so you know which ones to search.

| File | Read it when |
|---|---|
| `feature-map.md` (10 KB) | you need to find where a feature lives (page, Core service, UI file, data, tests), or want the layer diagram |
| `recipes.md` (8 KB) | you are about to add an app, page, list, column, setting, service, icon, alert or widget option, or change model data |
| `architecture.md` (70 KB: search) | file map (one line per class: search the class name), data folder, model data, performance, theming, one section per area |
| `decisions.md` (36 KB: search) | before UI work, the section of the page or feature you change: product and UI choices already agreed |
| `design-system.md` (24 KB: search) | you need a size, spacing, radius, icon, motion or color token, or a shared component |
| `conventions.md` | you build or change a list, card, label or page name (column order, widths, sorting, wording) |
| `typography.md` | you write or change any text (helpers, weights, fonts, number style, overflow) |
| `testing.md` | writing tests: UI test rules, the seams that replace the home folder, network and OS, coverage, screenshots |
| `adding-apps.md` | you edit `default/apps.json` (schema) |
| `releasing.md` | you cut a release, change packaging or the build setup (SDK, package versions) |
| `install-macos.md` | how a Mac user installs the app today and what is missing for a smooth install |
| `fonts-inventory.md` | which family, size and weight each place on screen uses (and what differs from the ladder) |
| `font-color-comparison.md` | font and color per place, side by side with Fluent UI 2 and Material 3 |
| `plan-languages.md` | plan, not started: a UI in more than one language |
| `plan-plugin-updates.md` | plan, not started: checking plugins for updates and updating them, per app group |
| `history/` | finished plans (settings and models; tray, widget and alerts, with what is left to verify by hand). Reference only: the docs above hold what is true now |
| `mockups/` | the agreed tray, widget and alert mockups (PNG, with the HTML they were drawn from) |

Keep these in step with the code: a new or renamed file updates the file map in `architecture.md`; a new page, route or column kind also `feature-map.md` and `conventions.md`, in the same change.
