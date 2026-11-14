# Builds the installer for one platform with Velopack (vpk). Run on that platform: Windows for win-x64, Linux for linux-x64, macOS for osx-arm64/osx-x64.
# Needs: the .NET SDK, vpk (dotnet tool install -g vpk --version 1.2.161), and on Linux/macOS rsvg-convert (librsvg) for the icon.
# Output: Releases/ (Setup, portable zip, update packages). Upload is done by .github/workflows/release.yml, not here.
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-arm64', 'osx-x64')][string]$Runtime,
    [string]$Output = 'Releases',
    [string]$ReleaseNotes   # Markdown file (git-cliff output in the release workflow); shown by Velopack and on the GitHub release
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# MinVer works out the version from the git tag (v1.2.3); the target has to run, a plain -getProperty:Version only sees the SDK default
$version = (dotnet msbuild src/App/AiStackprint.App.csproj -t:MinVer -getProperty:MinVerVersion).Trim()
if ($LASTEXITCODE -or -not $version) { throw "Could not work out the version (MinVer)" }
$publish = "artifacts/publish/$Runtime"
$work = "artifacts/icons"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null

dotnet publish src/App -c Release -r $Runtime --self-contained -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed" }

$svg = 'src/App/Assets/app-icon.svg'
$common = @('-u', 'AiStackprint', '-v', $version, '-p', $publish, '-o', $Output, '--packTitle', 'AI Stackprint', '--packAuthors', 'AI Stackprint contributors')
if ($ReleaseNotes) { $common += @('--releaseNotes', $ReleaseNotes) }
switch -Wildcard ($Runtime) {
    'win-*' {
        vpk pack @common -e AiStackprint.exe -i src/App/Assets/app-icon.ico -r $Runtime
    }
    'linux-*' {
        rsvg-convert -w 512 -h 512 $svg -o "$work/app-icon-512.png"
        vpk pack @common -e AiStackprint -i "$work/app-icon-512.png" -r $Runtime --categories Development
    }
    'osx-*' {
        # .icns from the SVG: every size macOS asks for, then iconutil
        $set = "$work/AppIcon.iconset"
        New-Item -ItemType Directory -Force $set | Out-Null
        foreach ($s in 16, 32, 128, 256, 512) {
            rsvg-convert -w $s -h $s $svg -o "$set/icon_${s}x${s}.png"
            rsvg-convert -w ($s * 2) -h ($s * 2) $svg -o "$set/icon_${s}x${s}@2x.png"
        }
        iconutil -c icns $set -o "$work/AppIcon.icns"
        vpk pack @common -e AiStackprint -i "$work/AppIcon.icns" -r $Runtime
    }
}
if ($LASTEXITCODE) { throw "vpk pack failed" }
Write-Host "AI Stackprint $version for $Runtime is in $Output"
