# Design system

The reference for how AI Stackprint looks. Colors and fonts are defined in `Theme`; everything with a size comes from the tokens in `src/App/Ui/Tokens.cs`. Views use token names, never numbers: `DesignTokenTests` fails the build's tests on a literal size, spacing, radius or type size in `src/App/Ui`.

## Rules

- Write sizes with tokens: `Space.L`, `Radius.M`, `TextSize.Small`, `IconSize.S`, `Sizes.Bar`, `Widths.Field`, `Heights.Viewer`. Zero is fine as a number.
- Pick the nearest step on the scale; do not add a value for one place. A new shared value goes into `Tokens.cs` with a comment saying where it is used, and into the tables below.
- Geometry that belongs to one component (a chart's radius, the window's first size) is a named `private const` in that component, with a comment. It is not reused elsewhere.
- Type sizes are for a 14 px base. Pass them to `W.Txt`, `W.Title`, `W.Sel`, `W.Mono` or `W.Size`, which scale them with Settings › Appearance.
- Inputs size themselves from the font (`Theme.Inputs`): do not set a height or padding on a text box, drop-down, check box or radio button. Use `Theme.InputHeight` when something must match an input's height.
- Colors: resource keys through `W.Themed` only (see `Theme`). No hex in views.

## Tokens

### Spacing (`Space`)

| Token | px | Use |
|---|---|---|
| `Hair` | 1 | between segments of a stacked bar |
| `XXS` | 2 | lines of a tight label stack |
| `XS` | 4 | icon to its badge; legend line spacing |
| `S` | 6 | pills and chips in a row |
| `M` | 8 | default gap in a row or list; icon to its text |
| `L` | 12 | label to value; card header to body; padding of inputs |
| `XL` | 16 | between cards; button side padding; between legend items |
| `XXL` | 20 | card padding; between page sections |
| `XXXL` | 24 | page top and bottom margin |
| `Page` | 32 | page side margins |

### Corner radius (`Radius`)

| Token | px | Use |
|---|---|---|
| `XS` | 2 | chart columns (Fluent UI 2 small) |
| `S` | 4 | bars, small chips, code blocks (medium) |
| `M` | 4 | buttons, list rows, inputs: every control; Fluent's own controls too (`ControlCornerRadius`, set in `Theme.Inputs`) (medium) |
| `L` | 8 | dialogs and pop-ups (xLarge; `OverlayCornerRadius`) |
| `Tile` | 8 | icon tile of a card header (`Sizes.IconTile` × `Sizes.TileCorner`) |
| `Card` | 12 | cards (2xLarge) |
| `XL` | 12 | overlays (command palette, toasts) |
| `Pill` | 999 | dots, pills, round buttons |

### Type (`TextSize`, at the 14 px base)

| Token | px | Weight | Use |
|---|---|---|---|
| `Tiny` | 10 | Body | axis ticks, badges (Fluent caption 2) |
| `Caption` | 12 | Body | captions, chips, hints (caption 1) |
| `Small` | 12 | Body / Emphasis | secondary text, table cells, legends (Fluent has no 13: this is caption 1 too) |
| `Body` | 14 | Body / Emphasis | default text (body 1) |
| `Subtitle` | 16 | Heading | card titles (subtitle 2) |
| `Title` | 20 | Heading | section titles inside a page (subtitle 1) |
| `Page` | 24 | Heading | page titles (`W.H1`) (title 3) |
| `Display` | 32 | Heading | big numbers on cards (title 1) |

Weights come from Appearance (`W.Body`, `W.Emphasis`, `W.Heading`); never set a fixed weight.

### Icons (`IconSize`)

| Token | px | Use |
|---|---|---|
| `XS` | 12 | beside caption text (small spinner) |
| `S` | 16 | inline with text, in buttons and inputs |
| `M` | 18 | card headers (inside the icon tile) |
| `L` | 20 | side menu |

### Lines (`Stroke`)

`Thin` 1 (borders, dividers) · `Medium` 2 (rings, chart lines).

### Pieces (`Sizes`, `Widths`, `Heights`)

| Token | px | Use |
|---|---|---|
| `Sizes.Dot` | 8 | legend and status dots |
| `Sizes.Bar` / `BarThin` | 10 / 8 | bar charts / progress bars in dense rows |
| `Sizes.IconTile` | 32 | tinted square behind a card icon (glyph is `TileGlyph` of it) |
| `Sizes.LogoS` / `LogoM` / `LogoL` / `LogoXL` | 24 / 28 / 32 / 56 | app logos: charts, tables, headers, page titles |
| `Sizes.CompactControl` | 24 | pills, toolbar buttons (Fluent small control) |
| `Theme.InputHeight` | ≈32 (33 for Inter 14) | inputs and standard buttons (Fluent default control), measured from the font |
| `Sizes.NavItem` | 40 | side menu entries |
| `Sizes.LargeControl` | 40 | command palette search box (Fluent large control) |
| `Widths.Field` / `FieldM` / `FieldS` | 220 / 160 / 120 | drop-down in a settings row / in a filter row / number box |
| `Widths.Search` | 280 | the narrowest search in a filter tool bar row (narrower: its own row) |
| `Widths.Label*` | 80–220 | label columns of forms and key-value lists |
| `Widths.Dialog*` | 380 / 520 / 640 / 760 | dialog widths (760: the hook events diagram) |
| `Heights.List` / `ViewerS` / `Viewer` / `ViewerL` | 260 / 320 / 420 / 520 | scrolling lists and viewers |
| `Heights.Chart` | 240 | a line chart inside a card (the limit pace chart) |
| `Sizes.BarWidget` | 6 | the usage widget's bars (round ends) |
| `Sizes.WidgetButton` | 24 | the widget's gear and × |
| `Sizes.PaceMark` / `PaceMarkRise` | 2 / 3 | the even-pace mark across a widget bar, and how far it runs above and below it |
| `Sizes.ScreenEdge` / `NoticeRing` | 10 / 14 | the tray notice and alert pop-ups: from the work area's edges; their ring |
| `Widths.Widget` / `WidgetLabel` / `WidgetPercent` / `WidgetIndent` | 280 / 52 / 34 / 60 | the widget (and its settings and alert pop-ups), its name and percent columns, the line under a row |
| `Widths.Notice` / `SubOption` | 258 / 22 | the "Still running in the tray" notice; a check box under another |

### Motion (`Duration`, ms)
| Token | ms | Use |
|---|---|---|
| `Faster` | 100 | hover, press; a pop-up or the command palette closing |
| `Fast` | 150 | pop-ups, menus and drop-downs opening; the command palette opening; a dialog, toast or the detail panel closing |
| `Normal` | 200 | dialogs, toasts and the detail panel opening |
| `Gentle` | 250 | larger moves |
| `Slow` | 400 | charts growing |

Motions go through `Motion.Enter` (fade in, cubic ease-out) and `Motion.Leave` (fade out, cubic ease-in, stays hidden), with a small slide (`dy`) or zoom (`scale`): pop-ups slide `Space.XS` from the side they open on, toasts rise `Space.M`, the detail panel rises `Space.XXXL`, dialogs and the command palette zoom from 96 %. A fade alone sets no transform (a transform change makes lists inside recompute which rows are on screen). They move only opacity and the render transform (a redraw, never a new layout) and run on the window's frames, so they cost nothing once they end. Every pop-up gets its motion from `Motion.Install` (tooltips keep Fluent's own fade). Inside a page (`Motion.Parts`): clickable rows and chips ease to the hover color (`W.Hover`, `Faster`; buttons, toggle switches and expanders already move in Fluent); the selected item of the side menu, the theme switch and a segmented control glides its fill from the last choice while its icon and text stay put (`Motion.Glide`, `Normal`, ease in-out; groups are rebuilt on each change, so the last place is kept by group name); a number stepped with − or + comes in from the side it moved to (`Motion.Nudge`); a table group's chevron turns and its rows fade in when it is opened or closed (`Motion.Turn`, `Motion.Chevron`, `TableRow.Enter`). Across the app: another page fades in (`Normal`, no slide: moving a whole page made every list on it work out its rows on each frame; the same page rebuilt stays still); data replacing a loading icon fades in (`Reveal.Fade`, `Fast`); bars grow from nothing and number tiles count up, `Slow`, only when their numbers are new this session (`Motion.Grow`, `Motion.CountUp`, in `Motion.Charts`: the dashboard tiles and the day charts, one scale per bar, no layout); a row the user removes (an Add app row, a project in the side menu) fades as its height closes (`Motion.Remove`, `Fast`); a bookmark or error picked in a log glides into view when it is near (`Motion.ScrollIntoView`, `Gentle`), a far one (over one and a half screens) is jumped to; search results always jump (a glide built every step it passed: search took about 500 ms longer in `--perf`). Settings › Appearance › Animations (`animations`) turns them off; headless tests run without them (`Motion.Disabled`).

### Color (`Theme`)

Fixed in both themes: the accent (light `#1450A0`, dark `#FD7E14`) and the card (light `#F8F9FA`, dark `#262626`). Every other color is worked out from them so that normal text passes WCAG 2.2 AAA (7:1; the app's minimum is `Contrast.Minimum`, 7.1) and `ThemeTests` checks it.

| Role | Light | Dark | Rule |
|---|---|---|---|
| Text | `#13161A` | `#F8F9FA` | strongest step |
| Muted text | `#40454A` | `#BEC3C9` | the faintest gray at 7.1:1 on every surface and tint (`Theme.TextBackgrounds`) |
| Link | `#004190` | `#FFB066` | accent hue at 7.1:1 on card and main; a custom accent is moved just far enough (`Contrast.AtLeast`) |
| Main / side / top | `#EFF1F3` / `#E8EAEC` / `#EFF1F3` | `#1A1C1D` / `#1E1F20` / `#202123` | small OKLCH lightness steps from the card |
| Hover / selected / text selection | text 5% / accent 8% / `#1450A0` 16% | text 6% / accent 10% / `#74A8F4` 10% | the strongest tints that keep muted text at 7.1:1 |
| Danger fill (white text) | `#96000D` | `#96000D` | 7.1:1 at rest; hover and pressed only darker |

**Status: text and mark are two keys.** Text (`ErrorKey`, `WarningKey`, `SuccessKey`, `SuccessText`) reads at 7.1:1; marks (`ErrorMark`, `WarningMark`, `SuccessMark`: icons, bars, dots, chips' dots, borders) need only 3:1 (WCAG 1.4.11), so they can be more vivid. Hues are fixed in OKLCH (danger 27°, warning 80°, success 145°) and no chart uses them.

| | Light text | Light mark | Dark text | Dark mark |
|---|---|---|---|---|
| Danger | `#9D010E` | `#9D010E` | `#FD0105` | `#FD0105` |
| Warning | `#654702` | `#B17E00` | `#FAB72A` | `#FAB72A` |
| Success | `#025915` | `#019D2A` | `#58DD64` | `#58DD64` |

Danger text and mark are one color in each theme. A strong red cannot reach 7:1 on the dark card, so dark danger text is the one exception to AAA: pure red at 3.7:1, chosen to look like danger; it always comes with an icon or words that say what is wrong.

**Chart palette** (`Theme.LightChart` / `Theme.DarkChart`, keys `W.ChartKey(1)` to `(8)`, `Ap.Chart.1` to `.8`). Eight series colors per theme, for every chart and graph; light on `#F8F9FA`, dark on `#262626`. The hex is exact and the OKLCH is it rounded; `ThemeTests` keeps the two in step, so a change to one means working out the other (clamped to sRGB). A custom accent does not change them.

| Slot | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Light | `#1958BB` oklch(.48 .169 260) | `#994403` (.49 .131 49) | `#056A3E` (.46 .109 156) | `#7C4096` (.48 .144 315) | `#BC8302` (.65 .135 78) | `#058292` (.56 .095 210) | `#C02658` (.53 .190 8) | `#747D00` (.56 .127 115) |
| Dark | `#F7791A` oklch(.71 .180 50) | `#42ACFF` (.72 .154 246) | `#42DC9A` (.80 .159 160) | `#C888E3` (.72 .144 315) | `#F8C739` (.85 .158 88) | `#45C5CE` (.76 .110 202) | `#FC647B` (.70 .186 14) | `#ADBE5A` (.77 .127 118) |

Rules:
- Slot N is series N, in order; never reordered or cycled.
- A series' color follows the thing it shows, not its rank, so a filter or a new period never repaints it:
  - token series In/Out/Write/Read: slots 1–4 (`ChartIn` …);
  - levels System, Local, Project, User: slots 1–4 (the user may override them);
  - app categories: the slot of their place in `Theme.Categories` (agent 1 … custom 7), whatever order the chart lists them in;
  - log-graph sub-agent n: slot n (`W.AgentKey`);
  - Usage page apps: the slot an app takes the first time it shows (largest first), kept while the app runs;
  - models compared: their slot in the comparison (1–4);
  - otherwise the order the legend lists them (invoked by: 1–3, Skills/Plugins: 1–2, a single series: 1).
- More than 8 series: the rest are "Other" in muted gray (`W.ChartKey` past 8, an unknown category, sub-agent 9 and up). Never a new hue.
- Color is never the only cue: each chart has a legend, labels, markers or dashes.
- Text always uses the text tokens, never a series color. Light slot 5 is only 3.1:1 on the card.
- What `dataviz`'s palette validator says about it: neighbours stay ≥ 19 ΔE apart for normal vision. For color-blind readers the closest pairs are light 2/3 (6.6, protan) and dark 7/8 (7.6, deutan), so legends and labels matter there. The validator's own bands also flag light slot 6 (chroma .095) and dark slots for their lightness.

**Limit pace chart** (`PaceChart`, `LimitsView.Pace`): three stat cards (label, figure in `Title` size, one line with a mark: success dot, info icon or warning icon) on the hover tint, a legend that draws each line's style, then the chart (680 × 300 proportions, 240 to 360 tall). Your use: chart slot 1, solid; its forecast: slot 1, dotted; the weekly even pace: muted gray, dashed, 1 px (both pace lines are thin guides), like the "Now" line (a guide, not a series); the 5-hour pace: warning mark, finely dotted, running one window before and one after its band at the same slope (three band widths, clipped at the plot) so it stands out, with its window as a band of the same color (by choice: the limit you hit first; the one series in a status color), and the "Limit" point in it too; "Reset" at the end of the weekly pace line (100 % at the right end): above it, under the 5-hour name, or under the line when the "Limit" time is there. A card's success dot sits in a box as tall as an icon, so it lines up with the first line of text. Every series also has a legend or a label, so color is never the only cue.

## Components

Built in `W` (`Widgets*.cs`); use these instead of building the same thing again.

- **Page** (`W.Page`): margins `Page` × `XXXL`, sections `XXL` apart. The header row is `W.H1` with buttons on the right.
- **Card** (`W.Card`): surface color, `Radius.Card`, padding `XXL` on all four sides, no border. Cards sit `XL` apart.
- **Card header** (`W.Section` / `W.CardHeader`): every card with a title uses it. Icon on a tinted square (`Sizes.IconTile`, accent glyph `IconSize.M`), title at `TextSize.Subtitle` in the heading weight, optional buttons on the right that wrap. The header is one input tall with or without buttons, and the body starts `L` below it, so headers line up across a row of cards. The header is lifted by half of (input height − tile size), so the icon tile is `XXL` from the card's top edge, the same as from its left edge.
- **Rendered Markdown** (`MarkdownView`): H1 at `TextSize.Title`, H2 at `Subtitle`, smaller headings and body at `Body`, table cells at `Small`; headings in the heading weight.
- **Stat card** (dashboard tiles): card header, big number (`Display`, right-aligned, tabular figures), a `Sizes.Bar` stacked bar, and a dot legend.
- **Buttons** (`W.Btn`): one input tall, side padding `XL`, `IconSize.S` icon. Primary uses the accent class: 12% lighter under the pointer in both themes; pressed 12% darker on the light theme, 24% lighter on the dark one. Its text keeps at least 7.1:1 in every state (WCAG 2.2 AAA 1.4.6 covers hover and pressed text): on the light theme the rest color is deep enough for the lighter hover to pass, and a custom accent is moved just far enough (`Contrast.AtLeast`). Destructive uses `W.DangerBtn`; icon-only uses `W.IconBtn`.
- **Pills** (`W.Pills`): `Sizes.CompactControl` tall, `Radius.Pill`, `S` apart; the selected one is accent.
- **Segmented** (`W.Segmented`; `W.Toggles` where each is on or off on its own, as Group by): one choice among a few as one rounded group with a 1 px border, one input tall; the chosen one is filled with the selection color and accent text, like the theme switch.
- **Filter menu** (`FilterMenu`): a filter as a button with a chevron whose menu has a check box per value with its count; button and menu are spaced like a drop-down (card background, arrow 14 from the right edge; the list with an `XS` inner padding, rows one input tall with no gap, the box `L` into its row, lit under the pointer, at least as wide as the button); the last one on cannot be turned off. While some are off the button has the accent border; it shows no count. Under the tool bar one line lists what is on: **Status:** Installed, Not found **Category:** Agents, Editors **Group by:** Category **Shown:** 12 of 40 (labels in the emphasis weight). A page with filters ends its tool bar with a Reset icon that puts the search, filters and grouping back as the page first shows them; it is dimmed while nothing differs.
- **Search help** (`SearchHelp.Button`): an ⓘ icon button right after the search of the skill, plugin and command lists (a flyout: how words, quotes and `field:` match, with examples); `FilterBar.Build(search, help)` docks it at the right end of the search. It replaces the search box tooltip. The pop-up of an ⓘ button (this one, the levels one and the column header ones) is made by `InfoFlyout.Make` and looks like a dialog: the same card (`Dialogs.Card`: surface, thin border, radius, dialog shadow) and title row with a close button (`Dialogs.Header`), opened just below the button; its content is an `InfoPanel` that carries the title; a table column gets an ⓘ after its header through `Col.Help` (a function that builds the pop-up's content): `Levels.Help` for Level, `HeaderHelp` (`HeaderHelp.cs`: `Categories`, `InvokedBy`) for Category and Invoked by; the Event column opens a dialog (`Col.HelpOpen`, `Dialogs.HookEvents`) with the `HookFlow` diagram. Keep the text short: a name and one line each. A dialog that answers with an object closes with `null` on Esc and the close button (`Dialogs.Chrome(w, null, …)`), never `false`.
- **Filter tool bar** (`FilterBar<T>`, laid out by `ToolbarPanel`): every list page with filters uses it: the search fills the row left of the menus, Group by and Reset (`M` apart); when the search would be narrower than `Widths.Search` (280) it takes its own row and the rest goes under it. A page registers its fields once and passes the searched items to `Apply` on each change.
- **Inputs**: text, number and search boxes, drop-downs, check boxes and radio buttons share `Theme.InputHeight`; text sits in the middle and multi-line inputs keep equal gaps above the first and below the last line (`Theme.Inputs`). Text boxes, drop-downs (`ComboBox`, `FilterMenu`) and the date picker show the accent border under the pointer, with the same background and text; the Group by toggles are filled like a side menu item under the pointer, with its text color.
- **Search** (`W.SearchBox`): search icon inside, one input tall. Filter after typing pauses (`.OnPause`, 150 ms), not on every key; mark matches on `HighlightKey` with `Table.Marked` (or `Table.NameDesc(..., marks:)`).
- **Chips and status** (`W.Chip`, `W.Status`): `Caption` text, `Radius.Pill`.
- **Dots** (`W.Dot`, `W.DotText`): `Sizes.Dot`, sitting on the text baseline.
- **Charts**: bars use `Sizes.Bar` and `Radius.S`; series colors are the chart palette (`W.ChartKey`, see Color above); status colors are never series. The donut (`PieChart`) animates in once and when its values change.

**Usage widget** (`UsageWidget`, docs/mockups/tray-widget.png): a card of the surface color whose background alone takes `widgetOpacity` (text, bars, icons and the 1 px border stay solid; opaque under the pointer, over `Duration.Fast`), `Radius.L`, padding 10 / 12 / 12. Header: `Usage limits · now HH:mm` in caption, muted; gear and × at `IconSize.XS` in 24 px buttons. One block per app (name in Emphasis small, plan or `as of HH:mm` muted on the right; a 1 px line between blocks, 10 above and below), one row per window: 52 px name (small, muted), the bar, 34 px percent (Emphasis small, right). The bar: track `Subtle`, fill `Accent` (bars never change color), a 2 px mark in the text color at 55 % where an even pace would be. The line under it (caption, under the bar) is muted, or the warning text color with a warning icon when the limit comes before the reset. The tray notice and our alert pop-up use the same card (`TrayNotice.Card`): the ring and "AI Stackprint" in caption, the title in Emphasis, the text muted, the accent button first.

## Loading icons
- One icon everywhere: `DotsLoader`, a ring of dots with a bright head running round and a fading tail, all in the accent color (`Busy.Spinner(size, key)` takes another color key, e.g. on-accent for a spinner on an accent button).
- Big (`Sizes.Loader`, 56): a whole page that is loading (the dashboard before the first scan, centered under the title): 12 fine dots.
- Small (12 to 20 px, from 48 px up the big look is used): 8 larger dots, beside text and in cards (`Busy.Status`, `Busy.Loading`, `Busy.Inline`).
- It carries the `Busy.Class` marker; the --perf run waits for none on screen.

## Page header and text
- A page title row is `W.Head()` (a DockPanel as tall as a button) with the title and then the buttons, pills or nothing at the right: the title sits at the same height on every page.
- Text made with `W.Txt`, `W.Title` and `W.Mono` is a `CopyableText` (a `SelectableTextBlock`): drag to select, Ctrl+C to copy. Inside a button or a clickable row a drag would fight the click, so there it does not select; right-click on any text, there too, offers Copy (the selection, or the whole text). Text made with `new TextBlock` directly is only copied by right-click.
- A cell with long text (a command) shows at most 3 lines with an ellipsis, the whole text in its tooltip.
- A count of zero is shown in the muted color.

### Fluent UI 2

The scales follow Fluent UI 2 (the React v9 theme as we know it; not re-checked against Microsoft's pages): spacing ramp 2, 4, 6, 8, 12, 16, 20, 24, 32 with 8 as the default gap; corner radii 2 / 4 / 8 / 12 (small, medium for every control, xLarge for dialogs, 2xLarge for cards); type ramp 10, 12, 14, 16, 20, 24, 32 (caption 2 to title 1); the top bar is 40 px high with 8 px padding left and right and 14 px text; icon sizes 12, 16, 20; control heights 24 (small), 32 (default), 40 (large); Emphasis weight 600 (semibold) by default. Colors stay as defined in `Theme`. Inputs: a card-colored field with a divider-colored 1 px border, the accent border under the pointer and when focused (Fluent 2: brand focus), the same for the date picker, drop-downs and filter menus (`W.InputHover`); an open drop-down or filter menu and its list keep Fluent's neutral borders; Group by toggles take the side menu's hover: the primary button's rest fill and text color, its hover fill while pressed (`Theme`, `W.AccentHover`); a form row gap of 8. Dialogs and the command palette were checked in light and dark theme against the same scales. Not taken from Fluent 2: card shadows (cards are flat on the page color).

## Dialogs and pop-ups
- A dialog is a floating card, not a framed window (`Dialogs.Chrome`, used by `Shell` and `AddAppDialog`): no system frame, a transparent window with `Sizes.ShadowRoom` of room for the shadow, a 12 px card (`Radius.XL`) with a 1 px border and the elevation-16 shadow (`Theme.DialogShadow`, the same as a pop-up: the whole shadow must fit in `ShadowRoom`, or the window edge cuts it into hard bands; elevation 64 reaches about 96 px below the card), the title at `TextSize.Title` with a close button on the right, the body, then the buttons at the bottom right (secondary first, primary last). The card is dragged by its title; Esc and the close button close with `false`. A dialog's outer width is `Dialogs.OuterWidth(card)`.
- A dialog's card fades in and grows from 96 % (`Duration.Normal`), and fades out before the window closes (`Duration.Fast`; the window keeps the result it was closed with).
- Pop-ups that float over the page (the command palette, toasts) have the elevation-16 shadow (`Theme.PopupShadow`); menus, drop-downs and fly-outs take their corners (8) and shadow from Fluent (`OverlayCornerRadius`).

