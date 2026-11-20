# Installing AI Stackprint on a Mac

Status: the release workflow builds an Apple-silicon installer (`osx-arm64`, a Velopack `.pkg` and a portable zip). It is **not signed or notarized yet**, and nobody has built or run it on a Mac in this repository, so the steps below are the expected ones: check them on a Mac and fix this page. Intel Macs have no build yet (see below).

## For users

### 1. The installer from GitHub Releases (Apple silicon, M1 or newer)

1. Open [Releases](https://github.com/krurkrit/ai-stackprint/releases) and download the macOS `.pkg` (its name has `osx` and `Setup`).
2. Double-click it and follow the installer. The app goes to `~/Applications` (Velopack installs per user; no administrator password).
3. The first start is blocked by Gatekeeper because the app is not notarized ("cannot be opened because the developer cannot be verified"):
   - macOS 14 and older: right-click the app › **Open** › **Open**.
   - macOS 15 and newer: try to open it once, then System Settings › **Privacy & Security** › scroll down › **Open Anyway**.
   - Or remove the quarantine mark in Terminal: `xattr -dr com.apple.quarantine ~/Applications/AiStackprint.app` (the path is where the installer put it).
4. Later starts open normally. The app reads files under your home folder only, and never changes anything without a dialog.

The portable zip is the same app without an installer: unzip it, run `AiStackprint.app`, same Gatekeeper step.

### 2. From source (any Mac, including Intel)

```bash
brew install --cask dotnet-sdk          # or the .NET 10 SDK installer from dot.net
git clone https://github.com/krurkrit/ai-stackprint && cd ai-stackprint
dotnet run --project src/App
```

For an app you can keep: `dotnet publish src/App -c Release -r osx-arm64 --self-contained false` (use `osx-x64` on Intel); it needs the .NET 10 runtime on the machine.

### 3. Homebrew (planned)

`build/homebrew/ai-stackprint.rb` is a cask template. Once the project has a tap (`krurkrit/homebrew-tap`) and a notarized `.pkg`, `brew install --cask krurkrit/tap/ai-stackprint` installs it and Gatekeeper stays quiet. Until the `.pkg` is notarized a cask would only automate steps 1 to 3 above.

## For maintainers: making the Mac install smooth

What is missing for a normal "double-click and go" install:

| Gap | Fix |
|---|---|
| Gatekeeper warning | An Apple Developer ID (99 USD a year), then sign and notarize with `vpk pack`: `--signAppIdentity "Developer ID Application: NAME (TEAMID)" --signInstallIdentity "Developer ID Installer: NAME (TEAMID)" --notaryProfile <profile>` (create the profile once with `xcrun notarytool store-credentials`). In CI keep the certificates and the notary credentials as repository secrets, import them into a temporary keychain on the `macos-latest` runner, and pass the options from `build/pack.ps1`. |
| No Intel build | Add `{ os: macos-13, rid: osx-x64, channel: osx-x64 }` to the `pack` matrix of `.github/workflows/release.yml`, add `osx-x64` to the upload loop, and give `build/pack.ps1` the channel (`vpk pack -c`). Check that Velopack keeps the two Mac channels apart before enabling updates. |
| Not in Homebrew | Fill in `build/homebrew/ai-stackprint.rb` (version, sha256 of the released `.pkg`, the pkg identifier from `pkgutil --pkgs` after an install) and publish it in a tap; the release workflow can open a pull request on the tap with the new version and checksum. |
| Unverified on a Mac | Install the released `.pkg` on a clean Mac (or a macOS VM), run the app, check Settings › Appearance fonts (Inter and JetBrains Mono are not bundled; the Mac falls back to SF and Menlo), and note the file names `vpk` really produced in `docs/releasing.md`. |

Platform notes: the app finds the AI tools by their folders under `~` (for example `~/.claude`), so it needs no special permission. Moving logs to Trash uses the macOS Trash. Fonts: Inter, Noto Sans and JetBrains Mono are not bundled; on a Mac without them the text uses the system font and Menlo.
