# AI Stackprint
[![Coverage workflow](https://github.com/krurkrit/ai-stackprint/actions/workflows/coverage.yml/badge.svg)](https://github.com/krurkrit/ai-stackprint/actions/workflows/coverage.yml)
[![Codecov](https://codecov.io/gh/krurkrit/ai-stackprint/graph/badge.svg)](https://codecov.io/gh/krurkrit/ai-stackprint)
[![Coveralls](https://coveralls.io/repos/github/krurkrit/ai-stackprint/badge.svg?branch=main)](https://coveralls.io/github/krurkrit/ai-stackprint?branch=main)
[![Codacy Grade](https://app.codacy.com/project/badge/Grade/0ffdae29f57f4d53ac2de1699a68003e)](https://app.codacy.com/gh/krurkrit/ai-stackprint/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)
[![Codacy Coverage](https://app.codacy.com/project/badge/Coverage/0ffdae29f57f4d53ac2de1699a68003e)](https://app.codacy.com/gh/krurkrit/ai-stackprint/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_coverage)

Cross-platform inventory of AI apps, orchestrators, plugins and skills (Project / User / System). Avalonia 12 + .NET 10, free (MIT, see `LICENSE`).

Download the installer for Windows, Linux or macOS from [Releases](https://github.com/krurkrit/ai-stackprint/releases) (on a Mac see [docs/install-macos.md](docs/install-macos.md): the installer is not notarized yet, Gatekeeper needs one extra step), or run from source with `dotnet run --project src/App` (needs the .NET 10 SDK). See `AGENTS.md` for contributor rules and `docs/` for architecture and adding apps.

Colors follow the Snowy Owl design system. The UI font is Inter when installed (falls back to Noto Sans, then the system font): install it from https://rsms.me/inter/.

## Third-party assets
- Installer and update packages: [Velopack](https://github.com/velopack/velopack) (MIT)
- Text viewer/editor: [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and [TextMateSharp](https://github.com/danipen/TextMateSharp) (MIT)
- UI icons: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT)
- App logos: [LobeHub Icons](https://github.com/lobehub/lobe-icons) (MIT) and [Simple Icons](https://github.com/simple-icons/simple-icons) (CC0). Logos are trademarks of their owners and identify the apps only.
