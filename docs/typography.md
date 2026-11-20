# Typography

How text is chosen, sized and weighted. The size and weight tables are also in `design-system.md`; this page says which helper to call and what the user can change. Code: `src/App/Ui/Widgets.cs` (`Txt`, `Sel`, `Title`, `Mono`, `H1`, `Size`), `Theme.cs` (font stacks, scale, weights), `Tokens.cs` (`TextSize`).

## Rules

1. Never write a font size, family or weight in a view. Pass a `TextSize.*` token to a `W` helper; `DesignTokenTests` fails on a literal size.
2. Sizes are written for a 14 px base. `W.Size(px)` multiplies by `W.Scale` (Settings › Appearance › size, 12 to 18 px, divided by 14), so everything scales together.
3. Weights are never fixed: `W.Body` (default 400) for text, `W.Emphasis` (500) for emphasis, `W.Heading` (SemiBold, or Bold when Emphasis is SemiBold or more, or the user's heading weight) for titles. The user sets them in Appearance (300 to 700 each; the side menu has its own setting).
4. Fonts are not bundled or downloaded. Stack: the chosen family (default Inter), then Noto Sans, then the platform default (`Theme.Font`). Code, logs, paths and config values use `Theme.Mono` (default JetBrains Mono, then Cascadia Mono, Consolas, Menlo, monospace) at `Theme.MonoSize` (10 to 18, default 12.5 at the base size).
5. Text colour is a resource key through `W.Themed` (`Text`, `Muted`, `WarningKey`, `ErrorKey`, `Accent` …). No hex in views.

## Which helper

| Need | Call | Weight | Notes |
|---|---|---|---|
| Normal text | `Txt(s, TextSize.Body)` | Body | wraps by default |
| Emphasis, a name in a row or table | `Txt(s, size, true)` | Emphasis | the `bold` flag means Emphasis, not Bold |
| Secondary or hint text | `Txt(s, TextSize.Small or Caption, false, Muted)` | Body | muted colour key |
| Card title, header name, big number | `Title(s, TextSize.Subtitle or Title or Display)` | Heading | uses the heading font when set |
| Page title | `H1(s)` or `H1(parent, go, name)` | Heading | `TextSize.Page`; parent part is muted and clickable |
| Selectable text (copy by drag) | `Sel(s, size, bold)` | Body or Heading | not inside a click target |
| Path, command, code value | `Mono(s, TextSize.Small or Caption)` | Body | `Theme.Mono` |
| Name with a muted line under it | `Table.NameDesc(name, desc)` | Body/Emphasis + Caption | description is one line, ellipsis |
| Code viewer or editor | `CodeView` | n/a | `Theme.Mono`, `Theme.MonoSize` |

Numbers made only of digits and separators (up to 24 characters) automatically get tabular figures (`tnum`) so columns of numbers line up; do not set it yourself.

## Size ladder (14 px base)

`Tiny` 10 (axis ticks, badges) · `Caption` 12 (hints, chips, table header) · `Small` 12 (table cells, legends) · `Body` 14 · `Subtitle` 16 (card titles) · `Title` 20 · `Page` 24 · `Display` 32. Pick the nearest step; add a new step only with a comment in `Tokens.cs` and a row in `design-system.md`.

## Where text appears

| Place | Size and weight |
|---|---|
| Page title | `Page`, Heading |
| Card title (`W.Section`) | `Subtitle`, Heading |
| Section title inside a page | `Title` or `Subtitle` (`Title(...)`), Heading |
| Table header | `Caption`, Emphasis, Muted, one line with ellipsis, tooltip = full header |
| Table main value | `Body`, Emphasis; other cells `Small` or `Caption` |
| Side menu item | `Body`, `menuWeight`; section label `Tiny`, Muted |
| Big number on a tile | `Display`, Heading |
| Chips, badges | `Caption` or `Tiny` |
| Logs, paths, configs | Mono at `Theme.MonoSize` |

## Overflow

Table headers and one-line descriptions use `TextWrapping.NoWrap` with `TextTrimming.CharacterEllipsis` and a tooltip. Star columns clip their cells (`ClipToBounds`). Inputs size themselves from the font (`Theme.Inputs`, `Theme.InputHeight`); do not set a height on a text box or button.

## Settings that change type (`default/settings.json`)

One setting, `fonts` (`Settings.Fonts`), holds every font setting by key: `body`, `emphasis`, `heading`, `menu`, `log` (the roles in `FontRoles`). Each has an optional `parent` and three fields, `family`, `size`, `weight`. A field with no `mode` is its own `value` (`set`) when there is no parent and the parent's value (`link`) when there is one; `adjust` (the parent's plus `value`, size and weight only) is always written. Defaults: Emphasis adjusts Body's weight by +200 (600), Headings adjust Emphasis by +100 (700) and link the family, Side menu adjusts Body by +100 (500), Code & logs is its own.

- **Rules** (`Core/Services/FontRuleSet`): `Resolve(key)` gives family, size and weight, each fitted to that setting's own limits (size: clamp, then snap to the step that starts at min; weight: the nearest allowed value, a tie goes the way of the offset). Limits are the app's (`FontRoles`: Body 12–18 px, Code 10–18 px, weights 300–700 in steps of 100); a setting may override them with `limit` (`min`/`max`/`step` or `options`). Weights must be 1–1000. `WouldCycle`, `SetParent`, `SetMode`, `SetNumber` keep a look when the mode or parent changes; `Validate` names a loop, an unknown parent, a link without a parent or a bad value. A file that fails `Validate` loads with the default fonts (the file stays until a save).
- **Control** (`Ui/FontSetting`, `.Fields`): one row for any key, always all four inputs: parent drop-down with a chain icon, then family, size and weight, each behind a mode button (`↳` use the parent's, `±` adjust, `✎` set; disabled while there is no parent). The label is the page's. Loops are not offered in the parent list. A page builds one per role, keeps the list, and refreshes the others when one changes (what links to it moved).
- **Applied** by `Theme.Appearance`: Body size scales every `TextSize`; Emphasis, Headings and Side menu apply their family and weight, and their size as a scale over Body's (`W.EmphasisScale`, `HeadingScale`, `MenuScale`: 1 while they follow Body); Code & logs go to `Theme.Mono`; the Appearance page (`AppearanceSection`) shows the controls and a preview.
- **Saved** as `fonts` in `settings.json`: only what differs from the default, and nothing the parent already implies (`FontRuleSet.Compact`: no empty parent, no empty field, no mode that is the default one). A mode stays when the built-in default has another one, and an empty `parent` stays when the default has a parent, so a default can be turned off. Earlier 0.x builds had one flat key per value (`fontFamily`, `fontSize`, `bodyWeight`, …): they are dropped when loading, not converted (0.x formats may change freely; from 1.0 they need versions and upgrade steps, see AGENTS.md).

## Wording in text

Page and nav names: Dashboard, Apps, Plugins, Skills, Commands & Agents, Servers, Hooks, Usage, Models, Logs, Health, Settings. Pair order is "Plugins and skills" (never "Skills and plugins"). See `conventions.md`.
