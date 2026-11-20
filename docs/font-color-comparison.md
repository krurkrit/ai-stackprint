# Fonts and colors: AI Stackprint vs Fluent UI 2 vs Material 3

Format: `family size weight / color`. Colors are `light | dark` where they differ. Weights are numbers (400 Regular, 500 Medium, 600 Semibold, 700 Bold).

**Sources and confidence.** *App* column: read from the code (`Theme.cs`, `Tokens.cs`, `Widgets.cs`) and from this machine's `~/.ai-stackprint/settings.json` (Emphasis 500, accent default). *Fluent UI 2* and *Material 3* columns: the published type ramps and baseline color tokens as I know them, **not fetched or checked against the sites in this session**. Verify before using as a spec: fluent2.microsoft.design (typography, color) and m3.material.io (type scale, color roles). Material 3 colors are the baseline (purple) scheme; Fluent colors are the web "neutral" and brand-blue tokens.

## Reference

| | AI Stackprint | Fluent UI 2 | Material 3 |
|---|---|---|---|
| UI font | Inter (then Noto Sans, system) | Segoe UI (Segoe UI Variable on Windows 11) | Roboto |
| Mono font | JetBrains Mono | Cascadia Mono / Consolas | Roboto Mono (not part of the type scale) |
| Size steps | 10, 12, 14, 16, 20, 24, 32 | Caption2 10, Caption1 12, Body1 14, Subtitle2 16, Subtitle1 20, Title3 24, Title1 32 | Label S 11, Body S/Label M 12, Body M/Label L/Title S 14, Body L/Title M 16, Title L 22, Headline S 24, Headline L 32, Display S 36 |
| Weights | Body 400, Emphasis 500 (default 600), Heading 600 (default 700), Menu 500 | 400 text, 600 strong, titles | 400 body, 500 labels and Title M/S, 400 headlines |
| Text color | `#34383C` \| `#F8F9FA` | fg1 `#242424` \| `#FFFFFF` | on-surface `#1D1B20` \| `#E6E0E9` |
| Muted text | text at 76% \| 72% (`#63666A` \| `#B9BABC` on the card) | fg3 `#616161` \| `#ADADAD` | on-surface-variant `#49454F` \| `#CAC4D0` |
| Accent | `#1450A0` \| `#FD7E14` | brand `#0F6CBD` \| `#479EF5` (fills `#0F6CBD` \| `#115EA3`) | primary `#6750A4` \| `#D0BCFF` |
| Page background | main `#E9ECEF` \| `#24262A` | bg3 `#F5F5F5` \| `#141414` | surface `#FEF7FF` \| `#141218` |
| Card / surface | `#F8F9FA` \| `#18191B` | bg1 `#FFFFFF` \| `#292929` | surface container low `#F7F2FA` \| `#1D1B20` |
| Side menu background | `#DEE2E6` \| `#2E3035` | bg2 `#FAFAFA` \| `#1F1F1F` | surface container `#F3EDF7` \| `#211F26` |
| Border / divider | text at 16% \| 24% (`#D9DADC` \| `#4E4F51`) | stroke2 `#E0E0E0` \| `#525252`; stroke1 `#D1D1D1` \| `#666666` | outline-variant `#CAC4D0` \| `#49454F`; outline `#79747E` \| `#938F99` |
| Error | `#A52834` \| text `#F28B95` | danger fg `#B10E1C` \| `#DC626D` | error `#B3261E` \| `#F2B8B5` |
| Success | `#12633D` \| `#5FCB94` | success fg `#107C10` \| `#54B054` | no role (custom) |
| Warning | `#8A3F00` \| `#FFC107` | warning fg `#BC4B09` \| `#FDCFB4` | no role (custom) |

## By place

| Page | Place | App | Fluent UI 2 | Material 3 |
|---|---|---|---|---|
| All | Page title | Inter 24 600 / `#34383C` \| `#F8F9FA` | Segoe UI 24 (Title 3) 600 / fg1 | Roboto 24 (Headline S) 400 / on-surface |
| All | Breadcrumb parent | Inter 24 600 / muted | Segoe UI 24 600 / fg3 | Roboto 24 400 / on-surface-variant |
| All | Card title | Inter 16 600 / text | Segoe UI 16 (Subtitle 2) 600 / fg1 | Roboto 16 (Title M) 500 / on-surface |
| All | Section title in a page | Inter 16 or 20 600 / text | Segoe UI 20 (Subtitle 1) 600 / fg1 | Roboto 22 (Title L) 400 / on-surface |
| All | Body text | Inter 14 400 / text | Segoe UI 14 (Body 1) 400 / fg1 | Roboto 14 (Body M) 400 / on-surface |
| All | Secondary text, hints, counts | Inter 12 400 / muted | Segoe UI 12 (Caption 1) 400 / fg3 | Roboto 12 (Body S) 400 / on-surface-variant |
| All | Big number on a tile | Inter 32 600 / text | Segoe UI 32 (Title 1) 600 / fg1 | Roboto 36 (Display S) 400 / on-surface |
| All | Chips, status pills | Inter 12 500 / text on hover-tint fill | Segoe UI 12 (Caption 1) 400, strong 600 / fg2 | Roboto 14 (Label L) 500 / on-surface-variant, outline border |
| All | Buttons | Inter 14 400 / text; primary on-accent `#F8F9FA` \| `#141517` | Segoe UI 14 600 / fg1; primary white on brand | Roboto 14 (Label L) 500 / primary; filled on-primary `#FFFFFF` |
| All | Danger button | Inter 14 400 / `#FFFFFF` on `#A52834` | Segoe UI 14 600 / white on danger bg `#C50F1F` | Roboto 14 500 / on-error `#FFFFFF` on error |
| All | Text box, combo box, date picker | Inter 14 400 / text; border divider, accent on hover and focus | Segoe UI 14 400 / fg1; border stroke1, brand underline on focus | Roboto 16 (Body L) 400 / on-surface; outline `#79747E`, primary on focus |
| All | Input placeholder | Inter 14 400 / muted | Segoe UI 14 400 / fg4 `#707070` \| `#ADADAD` | Roboto 16 400 / on-surface-variant |
| All | Tooltip | Fluent control default 12-14 400 / text | Segoe UI 12 (Caption 1) 400 / fg1 on bg1 with shadow | Roboto 12 (Body S) 400 / inverse-on-surface `#F5EFF7` on inverse-surface `#322F35` |
| All | Toast | Inter 14 400 / text | Segoe UI 14 400 / fg1 on bg1 with shadow | Roboto 14 (Body M) 400 / inverse-on-surface on inverse-surface |
| All | Dialog title | Inter 20 600 / text | Segoe UI 24 (Title 3) 600 / fg1 | Roboto 24 (Headline S) 400 / on-surface |
| All | Dialog body | Inter 14 400 / text | Segoe UI 14 400 / fg1 | Roboto 14 (Body M) 400 / on-surface-variant |
| All | Tabs | Inter 14 Fluent default / text; selected underline accent | Segoe UI 14 400, selected 600 / fg1 | Roboto 14 (Title S) 500 / on-surface-variant; active primary |
| All | Side menu item | Inter 14 500 / text; selected tint of accent | Segoe UI 14 400, selected 600 / fg2 | Roboto 14 (Label L) 500 / on-surface-variant; active on-secondary-container |
| All | Side menu section label | Inter 10 400 / muted | Segoe UI 12 (Caption 1) 400 / fg3 | Roboto 14 (Title S) 500 / on-surface-variant |
| Apps, Skills, Plugins, Models | Table header | Inter 12 500 / muted | Segoe UI 14 600 / fg1 | Roboto 14 (Title S) 500 / on-surface |
| Apps, Skills, Plugins, Models | Table main value (name) | Inter 12-14 500 / text | Segoe UI 14 400 (name 600) / fg1 | Roboto 14 (Body M) 400 / on-surface |
| Apps, Skills, Plugins, Models | Table other cells | Inter 12 400 / text or muted | Segoe UI 14 400 / fg1, secondary fg3 | Roboto 14 (Body M) 400 / on-surface, secondary on-surface-variant |
| Apps, Skills, Plugins, Models | Row hover / selected | `#34383C` at 8% \| `#F8F9FA` at 10%; selected accent at 12% \| 14% | bg1 hover `#F5F5F5`; selected `#EBEBEB` | state layer on-surface 8%; selected secondary-container `#E8DEF8` |
| Dashboard | Chart axis ticks | Inter 10 400 / muted | Segoe UI 10 (Caption 2) 400 / fg3 | Roboto 11 (Label S) 500 / on-surface-variant |
| Dashboard | Chart legend | Inter 12 400 / muted | Segoe UI 12 400 / fg2 | Roboto 12 (Label M) 500 / on-surface-variant |
| Dashboard | Chart series | accent, `#D1590E` \| `#74A8F4`, `#12633D` \| `#5FCB94`, `#7A8087` \| `#9AA0A6` | categorical palette (brand, then 8 hues) | primary, secondary `#625B71`, tertiary `#7D5260`, outline |
| Logs, Config | Logs, code, config editor | JetBrains Mono 14 400 / text; syntax colors from TextMate | Cascadia Mono 14 400 / fg1 | Roboto Mono 14 400 / on-surface |
| All | Paths, commands in views | JetBrains Mono 12 400 (names 500) / text or muted | Cascadia Mono 12 400 / fg1 | Roboto Mono 12 400 / on-surface |
| Settings | Setting label and hint | Inter 14 400 / text; hint 12 muted | Segoe UI 14 400 (label 600) / fg1; hint 12 fg3 | Roboto 16 (Body L) 400 / on-surface; supporting 14 on-surface-variant |
| Settings | Links | Inter 14 400 / accent (`#1450A0` \| `#FFB066`) | Segoe UI 14 400 / brand link `#0F6CBD` \| `#479EF5` | Roboto 14 500 / primary |

## Where they differ most

- **Table text:** the app uses 12 px for most cells and headers; Fluent and Material both use 14 for table content and headers.
- **Strong weight:** the app's Emphasis is 500 here (default 600). Fluent strong text is 600; Material uses 500 for labels but keeps headlines at 400.
- **Titles:** the app's titles are 600 (default 700); Fluent 600; Material 400.
- **Accent:** the app is blue on light, orange on dark; Fluent is brand blue on both; Material is purple on both (baseline).
- **Dark background:** the app's cards are `#18191B` and its page is `#24262A` (the page is lighter than the cards); Fluent and Material make the page the darkest layer and cards lighter.
- **Font:** Inter reads larger than Segoe UI or Roboto at the same px size.
