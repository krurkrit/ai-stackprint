# Design system

The reference for how AI Stackprint looks. Colors and fonts come from Snowy Owl (`snowy-owl/docs/TOKENS.md`, applied in `Theme`); everything with a size comes from the tokens in `src/App/Ui/Tokens.cs`. Views use token names, never numbers: `DesignTokenTests` fails the build's tests on a literal size, spacing, radius or type size in `src/App/Ui`.

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
| `XS` | 2 | chart columns |
| `S` | 4 | bars, small chips, code blocks |
| `M` | 6 | buttons, list rows, inputs |
| `L` | 8 | cards and dialogs |
| `XL` | 12 | overlays (command palette, toasts) |
| `Pill` | 999 | dots, pills, round buttons |

### Type (`TextSize`, at the 14 px base)

| Token | px | Weight | Use |
|---|---|---|---|
| `Tiny` | 11 | Body | axis ticks, badges |
| `Caption` | 12 | Body | captions, chips, hints |
| `Small` | 13 | Body / Emphasis | secondary text, table cells, legends |
| `Body` | 14 | Body / Emphasis | default text |
| `Subtitle` | 15 | Heading | card titles |
| `Title` | 18 | Heading | section titles inside a page |
| `Page` | 24 | Heading | page titles (`W.H1`) |
| `Display` | 32 | Heading | big numbers on cards |

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
| `Sizes.CompactControl` | 32 | pills, toolbar buttons |
| `Theme.InputHeight` | ≈36 | inputs and standard buttons (measured from the font) |
| `Sizes.NavItem` | 40 | side menu entries |
| `Sizes.LargeControl` | 44 | command palette search box |
| `Widths.Field` / `FieldM` / `FieldS` | 220 / 160 / 120 | drop-down in a settings row / in a filter row / number box |
| `Widths.Label*` | 80–220 | label columns of forms and key-value lists |
| `Widths.Dialog*` | 380 / 520 / 640 | dialog widths |
| `Heights.List` / `ViewerS` / `Viewer` / `ViewerL` | 260 / 320 / 420 / 520 | scrolling lists and viewers |

## Components

Built in `W` (`Widgets*.cs`); use these instead of building the same thing again.

- **Page** (`W.Page`): margins `Page` × `XXXL`, sections `XXL` apart. The header row is `W.H1` with buttons on the right.
- **Card** (`W.Card`): surface color, `Radius.L`, padding `XXL`, no border. Cards sit `XL` apart.
- **Card header** (`W.Section` / `W.CardHeader`): every card with a title uses it. Icon on a tinted square (`Sizes.IconTile`, accent glyph `IconSize.M`), title at `TextSize.Subtitle` in the heading weight, optional buttons on the right that wrap. The header is one input tall with or without buttons, and the body starts `L` below it, so headers line up across a row of cards.
- **Rendered Markdown** (`MarkdownView`): H1 at `TextSize.Title`, H2 at `Subtitle`, smaller headings and body at `Body`, table cells at `Small`; headings in the heading weight.
- **Stat card** (dashboard tiles): card header, big number (`Display`, right-aligned, tabular figures), a `Sizes.Bar` stacked bar, and a dot legend.
- **Buttons** (`W.Btn`): one input tall, side padding `XL`, `IconSize.S` icon. Primary uses the accent class; destructive uses `W.DangerBtn`; icon-only uses `W.IconBtn`.
- **Pills** (`W.Pills`): `Sizes.CompactControl` tall, `Radius.Pill`, `S` apart; the selected one is accent.
- **Inputs**: text, number and search boxes, drop-downs, check boxes and radio buttons share `Theme.InputHeight`; text sits in the middle and multi-line inputs keep equal gaps above the first and below the last line (`Theme.Inputs`).
- **Search** (`W.SearchBox`): search icon inside, one input tall. Filter after typing pauses (`.OnPause`, 150 ms), not on every key; mark matches on `HighlightKey` with `Table.Marked` (or `Table.NameDesc(..., marks:)`).
- **Chips and status** (`W.Chip`, `W.Status`): `Caption` text, `Radius.Pill`.
- **Dots** (`W.Dot`, `W.DotText`): `Sizes.Dot`, sitting on the text baseline.
- **Charts**: bars use `Sizes.Bar` and `Radius.S`; series colors are the `Chart*`, level and category keys in `Theme`. The donut (`PieChart`) animates in once and when its values change.
