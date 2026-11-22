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
| `Widths.Label*` | 80–220 | label columns of forms and key-value lists |
| `Widths.Dialog*` | 380 / 520 / 640 | dialog widths |
| `Heights.List` / `ViewerS` / `Viewer` / `ViewerL` | 260 / 320 / 420 / 520 | scrolling lists and viewers |

## Components

Built in `W` (`Widgets*.cs`); use these instead of building the same thing again.

- **Page** (`W.Page`): margins `Page` × `XXXL`, sections `XXL` apart. The header row is `W.H1` with buttons on the right.
- **Card** (`W.Card`): surface color, `Radius.Card`, padding `XXL` on all four sides, no border. Cards sit `XL` apart.
- **Card header** (`W.Section` / `W.CardHeader`): every card with a title uses it. Icon on a tinted square (`Sizes.IconTile`, accent glyph `IconSize.M`), title at `TextSize.Subtitle` in the heading weight, optional buttons on the right that wrap. The header is one input tall with or without buttons, and the body starts `L` below it, so headers line up across a row of cards. The header is lifted by half of (input height − tile size), so the icon tile is `XXL` from the card's top edge, the same as from its left edge.
- **Rendered Markdown** (`MarkdownView`): H1 at `TextSize.Title`, H2 at `Subtitle`, smaller headings and body at `Body`, table cells at `Small`; headings in the heading weight.
- **Stat card** (dashboard tiles): card header, big number (`Display`, right-aligned, tabular figures), a `Sizes.Bar` stacked bar, and a dot legend.
- **Buttons** (`W.Btn`): one input tall, side padding `XL`, `IconSize.S` icon. Primary uses the accent class: under the pointer one step further from its text color (darker on the light theme, lighter on the dark one), pressed one more, so every state shows a change and its text keeps at least 7.1:1 (WCAG 2.2 AAA 1.4.6 covers hover and pressed text; a custom accent is moved just far enough, `Contrast.AtLeast`). Destructive uses `W.DangerBtn`; icon-only uses `W.IconBtn`.
- **Pills** (`W.Pills`): `Sizes.CompactControl` tall, `Radius.Pill`, `S` apart; the selected one is accent.
- **Segmented** (`W.Segmented`; `W.Toggles` where each is on or off on its own, as Group by): one choice among a few as one rounded group with a 1 px border, one input tall; the chosen one is filled with the selection color and accent text, like the theme switch.
- **Filter menu** (`FilterMenu`): a filter as a button with a chevron whose menu has a check box per value with its count; the last one on cannot be turned off. While some are off the button has the accent border; it shows no count. Under the tool bar one line lists what is on: **Status:** Installed, Not found **Category:** Agents, Editors **Group by:** Category (labels in the emphasis weight). A page with filters ends its tool bar with a Reset icon, dimmed while nothing is filtered or searched.
- **Inputs**: text, number and search boxes, drop-downs, check boxes and radio buttons share `Theme.InputHeight`; text sits in the middle and multi-line inputs keep equal gaps above the first and below the last line (`Theme.Inputs`). Text boxes, drop-downs (`ComboBox`, `FilterMenu`) and the date picker show the accent border under the pointer, with the same background and text; the Group by toggles are filled like a side menu item under the pointer, with its text color.
- **Search** (`W.SearchBox`): search icon inside, one input tall. Filter after typing pauses (`.OnPause`, 150 ms), not on every key; mark matches on `HighlightKey` with `Table.Marked` (or `Table.NameDesc(..., marks:)`).
- **Chips and status** (`W.Chip`, `W.Status`): `Caption` text, `Radius.Pill`.
- **Dots** (`W.Dot`, `W.DotText`): `Sizes.Dot`, sitting on the text baseline.
- **Charts**: bars use `Sizes.Bar` and `Radius.S`; series colors are the `Chart*`, level and category keys in `Theme`. The donut (`PieChart`) animates in once and when its values change.

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

The scales follow Fluent UI 2 (the React v9 theme as we know it; not re-checked against Microsoft's pages): spacing ramp 2, 4, 6, 8, 12, 16, 20, 24, 32 with 8 as the default gap; corner radii 2 / 4 / 8 / 12 (small, medium for every control, xLarge for dialogs, 2xLarge for cards); type ramp 10, 12, 14, 16, 20, 24, 32 (caption 2 to title 1); the top bar is 40 px high with 8 px padding left and right and 14 px text; icon sizes 12, 16, 20; control heights 24 (small), 32 (default), 40 (large); Emphasis weight 600 (semibold) by default. Colors stay as defined in `Theme`. Inputs: a card-colored field with a divider-colored 1 px border, the accent border under the pointer and when focused (Fluent 2: brand focus), the same for the date picker, drop-downs and filter menus (`W.InputHover`); Group by toggles take the side menu's hover: the primary button's rest fill and text color, its hover fill while pressed (`Theme`, `W.AccentHover`); a form row gap of 8. Dialogs and the command palette were checked in light and dark theme against the same scales. Not taken from Fluent 2: card shadows (cards are flat on the page color).

## Dialogs and pop-ups
- A dialog is a floating card, not a framed window (`Dialogs.Chrome`, used by `Shell` and `AddAppDialog`): no system frame, a transparent window with `Sizes.ShadowRoom` of room for the shadow, a 12 px card (`Radius.XL`) with a 1 px border and the elevation-64 shadow (`Theme.DialogShadow`), the title at `TextSize.Title` with a close button on the right, the body, then the buttons at the bottom right (secondary first, primary last). The card is dragged by its title; Esc and the close button close with `false`. A dialog's outer width is `Dialogs.OuterWidth(card)`.
- Pop-ups that float over the page (the command palette, toasts) have the elevation-16 shadow (`Theme.PopupShadow`); menus, drop-downs and fly-outs take their corners (8) and shadow from Fluent (`OverlayCornerRadius`).

