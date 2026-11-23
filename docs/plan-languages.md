# Plan: UI in more than one language (later)

Not started. Nothing here is decided yet: languages, the string mechanism and the interface are to be agreed first (`decisions.md`). Delete this file when the last phase is merged.

Each phase is one Conventional commit (or PR), ends with `dotnet format`, a 0-warning build, all tests passing, and the docs updated.

## Today
Every UI string is written in the code. A rough count finds at least 550 string literals in the views (about 330 plain, about 240 built with `$"..."`), and more through helper methods. Core services also produce text the user sees (health findings, error messages). There is no translation mechanism.

## Approach (proposed)
- **Strings:** one JSON file per language, embedded like the other data files. `en` is the source and the fallback: a missing key shows the English text, so a half-translated language still works. A class such as `T.Get("Search apps")` or `T.Get("{0} of {1}", a, b)` replaces direct strings.
- **Keys:** the English text is the key, so existing strings can be migrated in place. Translatable text avoids `$"..."` and uses numbered placeholders, because word order differs between languages. Plurals need their own rule (one item, several items).
- **Setting:** `language` in `default/settings.json`, default "system". Pages are rebuilt on every visit, so a change needs only a refresh.
- **Core text:** services return codes or keys and the UI translates them, so Core knows nothing about languages.
- **Layout:** widths already come from tokens; check columns and buttons with longer languages (German).
- **Fonts:** the list falls back to the platform default; test Thai and CJK.
- **Not translated:** app and tool names, log contents, file paths, and `apps.json` descriptions (unless a translated field is added there).
- **Right-to-left** (Arabic, Hebrew): needs extra layout work (`FlowDirection`); not in the first languages.

## Which languages
No source gives developer counts by language (checked: JetBrains and Stack Overflow 2025 surveys, VS Code locale docs). The order below is an inference from developers by country (GitHub Octoverse 2025, GitHub accounts, not professional developers; figures from a third-party article that cites it, not checked in GitHub's report):

| Country | GitHub accounts (2025) |
|---|---|
| United States | about 28 million |
| India | about 21.9 million |
| China | about 10.7 million |
| Brazil | about 6.9 million |
| United Kingdom | about 4.8 million |
| Japan | about 4.5 million |
| Germany | about 4.4 million |
| Indonesia | about 4.4 million |

GitHub's 2026 Octoverse reports the United States at 38.3 million and India at 32 million, and Brazil adding 3.28 million, the third most.

Proposed order: Simplified Chinese, Brazilian Portuguese, Japanese; then German, Indonesian, Spanish, Korean, Russian, French. India mostly works in English tools, so Hindi is lower priority.

**To check before choosing:** install counts of the Language Pack extensions on the VS Code Marketplace and Open VSX, sorted by installs: the best measure of which UI languages developers switch to.

## Phases
1. **Mechanism:** the `T` class, the `language` setting, the `en` file and a lint that fails on new hard-coded strings in views. No visible change.
2. **Migrate pages** one at a time, starting with the shared widgets and the Apps page. Core text moves to keys.
3. **Add each language**, with a language picker in Settings.
4. **Check** layout (long text), fonts, and right-to-left if wanted.

## Tests
- Every key in a language exists in `en`, and no key is unused.
- A missing key falls back to English.
- Placeholders and plural forms match between languages.
- The lint for hard-coded strings.
- Headless UI tests with a long test language to catch clipping.
