# Releasing

Installers are built with [Velopack](https://velopack.io) (MIT): `vpk` packs the published app, and the app calls `VelopackApp.Build().Run()` first thing in `Main` so the installer can run its install, update and uninstall hooks.

## Publish a release
The version is the git tag; no file holds it. [MinVer](https://github.com/adamralph/minver) (Apache-2.0, build-only) reads the latest `v*` tag on every build: a tagged commit builds as that version (`0.2.0`), later commits as the next patch with a pre-release label (`0.2.1-alpha.0.3`: 3 commits after `v0.2.0`), so a test build never looks like a release. Check what this checkout builds as: `dotnet msbuild src/App/AiStackprint.App.csproj -restore -t:MinVer -getProperty:MinVerVersion`.

1. Tag the commit on `main` and push the tag:
   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```
2. `.github/workflows/release.yml` runs on the tag. It checks that MinVer works out the tag's version, runs the tests, builds on Windows, Linux and macOS, and publishes one GitHub release with all three and its release notes.

## Old releases
After publishing, the workflow keeps the newest 5 releases (`KEEP` in the "Delete old releases" step) and deletes older ones with their files. Tags stay: history, git-cliff and MinVer use them. Drafts are not touched.

## Release notes
[git-cliff](https://git-cliff.org) writes them from the commits since the previous `v*` tag (`cliff.toml`). They go into the installer packages (`vpk pack --releaseNotes`) and the GitHub release text.

- Merge PRs with squash, and title them as [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `feat:` → New, `fix:` → Fixed, `perf:` → Faster; `refactor`, `style`, `test`, `build`, `ci`, `chore` and `docs` are left out. `feat!:` or a `BREAKING CHANGE:` footer marks a breaking change.
- Commits from before this (plain "Add …", "Fix …") are sorted by their first word.
- Preview the next release's notes: `npx git-cliff --unreleased --strip all` (or install git-cliff).

Users with the update check turned on (Settings › Updates) then see the new version and a link to its release page.

## What each platform gets
Windows file names are from a local build; Linux and macOS names come from vpk on those platforms (not built locally yet).

| Platform | Files | Install |
|---|---|---|
| Windows (x64) | `AiStackprint-win-Setup.exe`, `AiStackprint-win-Portable.zip` | Per user, no admin; Start menu and desktop shortcuts; uninstall from Settings › Apps |
| Linux (x64) | an `.AppImage` | Make it executable and run it |
| macOS (Apple silicon) | a `.pkg` installer and a portable zip | Open the .pkg |

Each also gets `*-full.nupkg` and `releases.<channel>.json`: Velopack's update packages (for in-app updates, if added later).

## Build one locally
Needs the .NET SDK and `vpk` (`dotnet tool install -g vpk --version 1.2.161`); on Linux and macOS also `rsvg-convert` (librsvg) to make the icon from `src/App/Assets/app-icon.svg`.
```bash
pwsh build/pack.ps1 -Runtime win-x64
```
Output goes to `Releases/` (ignored by git). Each platform must be built on that platform. Without an installer (framework-dependent): `dotnet publish src/App -c Release -r <win-x64|linux-x64|osx-arm64> --self-contained false`.

## Build setup
.NET 10 (LTS), pinned in `global.json`. Package versions are set once in `Directory.Packages.props` (exact versions; projects list packages without versions); shared build settings in `Directory.Build.props`. `SonarAnalyzer.CSharp` is a build-only package; the rules turned off are listed with reasons in `.editorconfig`.

## Not signed yet
- Windows: SmartScreen warns "unknown publisher" (More info › Run anyway). Fix: a code-signing certificate or Azure Trusted Signing, passed to `vpk pack` with `--signParams` or `--azureTrustedSignFile`.
- macOS: Gatekeeper blocks unsigned apps; users right-click › Open (macOS 15: System Settings › Privacy & Security › Open Anyway). Fix: an Apple Developer ID plus notarization (`vpk pack` signing options on macOS). Steps, the missing Intel build and a Homebrew cask template: `install-macos.md`.
