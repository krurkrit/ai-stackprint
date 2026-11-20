# Fonts: family, size, weight everywhere

Settings are named by their key in `fonts` (see `typography.md`): each can link to a parent setting or adjust its value.

What the app draws today. Sizes are for the default 14 px base (Settings › Appearance › Font size scales every UI size by `size / 14`; the code-view sizes are separate). Rules and helpers: `typography.md`. Tokens: `src/App/Ui/Tokens.cs` (`TextSize`).

## Families

| Role | Setting (default) | Stack | Used by |
|---|---|---|---|
| UI | `fonts.body.family` (Inter) | chosen, Inter, Noto Sans, platform default | everything except below |
| Headings | `fonts.heading.family` ("" = same as UI) | same stack | `Title`, `H1` only |
| Mono | `fonts.log.family` (JetBrains Mono) | chosen, JetBrains Mono, Cascadia Mono, Consolas, Menlo, monospace | paths, commands, code values, logs, config editor, markdown code |

## Weights (default → on this machine)

| Name | Setting | Default | This machine's `settings.json` | Used for |
|---|---|---|---|---|
| Body | `fonts.body.weight` | 400 | 400 | normal text, buttons, inputs |
| Emphasis | `fonts.emphasis.weight` | 600 | 500 | names in rows, table headers, chips, `Txt(..., bold: true)` |
| Heading | `fonts.heading.weight` (0 = derived) | Bold 700 (Emphasis ≥ 600), else 600 | 600 | page, card and section titles, big numbers |
| Menu | `fonts.menu.weight` | 500 | 500 | side menu items |
| Mono | `fonts.log.weight` | 400 | 400 | all Mono text |


## Size steps

`Tiny` 10 · `Caption` 12 · `Small` 12 · `Body` 14 · `Subtitle` 16 · `Title` 20 · `Page` 24 · `Display` 32. Uses in views: Small 195, Caption 71, Body 52, Tiny 7, Subtitle 9, Page 5, Title 5, Display 3.

## By place

| Place | Family | Size | Weight | Color |
|---|---|---|---|---|
| Page title (`H1`, breadcrumb) | UI / heading | 24 | Heading | Text; parent part Muted |
| Card title (`W.Section`) | UI / heading | 16 | Heading | Text |
| Section title inside a page | UI / heading | 16 or 20 | Heading | Text |
| Dialog title | UI / heading | 20 | Heading | Text |
| Big number on a tile, donut centre | UI / heading | 32 | Heading, tabular digits | Text |
| Body text, descriptions | UI | 14 | Body | Text |
| Table header | UI | 12 | Emphasis | Muted |
| Table main value (name) | UI | 12 (`Small`) or 14 | Emphasis | Text |
| Table other cells | UI | 12 | Body | Text / Muted |
| Name with description line (`Table.NameDesc`) | UI | name 12–14, description 12 | Emphasis / Body | Text / Muted |
| Counts, legends, hints, notes | UI | 12 | Body | Muted |
| Chips, badges, status pills | UI | 12 | Emphasis | Text |
| Side menu item | UI | 14 | Menu | Text |
| Side menu section label | UI | 10 | Body | Muted |
| Chart axis ticks, graph times, colour hints | UI | 10 | Body | Muted |
| Buttons (`Btn`, `DangerBtn`) | UI | 14 (`ControlContentThemeFontSize`) | Body (Fluent default) | Text / on-accent |
| Small buttons (log "Show original", "Source") | UI | 12 | Body | Text |
| Text boxes, combo boxes, date picker | UI | 14 | Body | Text; placeholder Muted |
| Tabs | UI | 14 (`TabItemHeaderFontSize`) | Fluent default | Text |
| Tooltips, toasts | UI | Fluent default / 14 | Fluent default | Text |
| Paths, commands, config keys in views | Mono | 12 (`Small`) or 12 (`Caption`) | Body, or Emphasis for names | Text / Muted |
| Logs, readable-log steps | Mono | `fonts.log.size` (14) | `fonts.log.weight` (400); bold parts Emphasis | Text |
| Config editor, code viewer (`CodeView`) | Mono | `fonts.log.size` (14) | `fonts.log.weight` (400) | syntax colors |
| Markdown | UI; code Mono | h1 20, h2 16, text 14, code `fonts.log.size` | headings Heading; bold runs Bold | Text |

## Things that differ from the ladder

- Most secondary text is 12 px, not 14: table cells, legends, hints, counts, chips, table headers. Only body text, buttons, inputs, menu items and tabs are 14.
- Fluent control text (buttons, inputs, tabs, tooltips) takes its weight from the Fluent theme (Regular, 400), not from `fonts.body.weight`/`fonts.emphasis.weight`.
- Markdown bold runs and log bold runs use `FontWeight.Bold` (700) fixed, not `Emphasis`.
- `Sel` (selectable text) uses Heading, not Emphasis, for its bold flag.

## Fluent UI 2 recommendation: where the app agrees and where it does not

Fluent UI 2 type ramp (web/Windows): Caption 2 10/14, Caption 1 12/16, Body 1 14/20, Subtitle 2 16/22, Subtitle 1 20/28, Title 3 24/32, Title 1 32/40. Weights: Regular 400 for text; Semibold 600 for "strong" body text, subtitles and titles; Medium 500 and Bold 700 are rare. Font: Segoe UI (Segoe UI Variable on Windows 11), system font elsewhere. The app's `TextSize` steps are named after this ramp (see comments in `Tokens.cs`).

| Topic | Fluent UI 2 | App | Verdict |
|---|---|---|---|
| Size steps | 10, 12, 14, 16, 20, 24, 32 | same; there is no 13 px step | agrees |
| Body size | 14 | 14 | agrees |
| Secondary text | Caption 1, 12 | `Small`/`Caption`, 12 | agrees by the ramp, but it is most of the screen here (tables, hints, chips), which reads small; Fluent keeps 12 for secondary text only and 14 for primary table content |
| Caption 2 (10) | for rare, non-essential labels | menu section label, axis ticks, colour hints | agrees; keep it to those |
| Strong text | Semibold 600 | Emphasis default 600; **500 on this machine** | 500 is lighter than Fluent's strong text; 600 matches it |
| Titles | Semibold 600 (Title/Subtitle) | Heading = Bold 700 when Emphasis ≥ 600 | heavier than Fluent; Semibold 600 is the recommendation (`fonts.heading.weight` 600 gives it) |
| Menu items | Regular 400, Semibold 600 when selected | `fonts.menu.weight` 500, one weight for all | Segoe UI has no 500 and draws it Regular; Inter does have it |
| Bold in markdown and logs | Semibold 600 | fixed Bold 700 | heavier than Fluent |
| Line height | set per step (14/20, 12/16 …) | not set; the font's own | differs; dense rows may look tighter or looser than Fluent |
| Font family | Segoe UI (Variable) | Inter, then Noto Sans, then system | differs on purpose; Inter is wider and looks larger at the same px |
| Mono | Cascadia Mono / Consolas | JetBrains Mono, then Cascadia Mono, Consolas | differs on purpose |
| Minimum size | 12 for readable text | 10 at lowest, 12 common | agrees |

Concerns to decide: raise table cells from 12 to 14 (Fluent's Body 1 for primary content), set Emphasis back to 600 and Heading to 600, and set line heights per step.
