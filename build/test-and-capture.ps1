<#
.SYNOPSIS
  Runs every test (all features) and captures screenshots of every page and feature, light and dark.
.DESCRIPTION
  The screenshot tests (tests/AiStackprint.App.Tests/ScreenshotTests.cs) draw the real app on Avalonia's headless Skia platform and write PNG files
  only when STACKPRINT_SHOTS names a folder; this script sets it, runs the whole test suite with a TRX log, and writes a report next to the pictures:
    <Out>/index.html    summary (passed / failed / skipped, failed test names) and a gallery, light beside dark
    <Out>/results/      the TRX log
    <Out>/*.png         page-*, fonts-*, feature-*
  The exit code is the test run's, so it can gate a build.
.EXAMPLE
  pwsh build/test-and-capture.ps1 -Open
.EXAMPLE
  pwsh build/test-and-capture.ps1 -Out C:\temp\shots -Filter "FullyQualifiedName~Pages"
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot "..\artifacts\screenshots\$(Get-Date -Format 'yyyyMMdd-HHmmss')"),
    [string]$Filter = "",
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$env:STACKPRINT_SHOTS = $Out

Write-Host "Running all tests; screenshots go to $Out"
$args = @('test', '--nologo', '-v', 'q', '--logger', 'trx;LogFilePrefix=results', '--results-directory', (Join-Path $Out 'results'))
if ($Filter) { $args += @('--filter', $Filter) }
Push-Location $root
try { & dotnet @args; $code = $LASTEXITCODE } finally { Pop-Location }

# Summary from the TRX logs (one per test project)
$passed = 0; $failed = 0; $skipped = 0; $failures = @()
Get-ChildItem (Join-Path $Out 'results') -Filter *.trx -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    [xml]$trx = Get-Content $_.FullName
    foreach ($r in $trx.TestRun.Results.UnitTestResult) {
        switch ($r.outcome) {
            'Passed' { $passed++ }
            'Failed' { $failed++; $failures += $r.testName }
            default { $skipped++ }
        }
    }
}

# Gallery: one row per picture name, light beside dark
$pngs = Get-ChildItem $Out -Filter *.png | Sort-Object Name
$groups = $pngs | Group-Object { $_.BaseName -replace '-(light|dark)$', '' }
$rows = foreach ($g in $groups) {
    $cells = foreach ($theme in 'light', 'dark') {
        $f = $g.Group | Where-Object { $_.BaseName -like "*-$theme" }
        if ($f) { "<td><a href=`"$($f.Name)`"><img src=`"$($f.Name)`" loading=`"lazy`"></a><div>$theme</div></td>" } else { '<td></td>' }
    }
    "<tr><th>$($g.Name)</th>$($cells -join '')</tr>"
}
$failList = if ($failures) { '<ul>' + (($failures | ForEach-Object { "<li>$([System.Net.WebUtility]::HtmlEncode($_))</li>" }) -join '') + '</ul>' } else { '' }
$status = if ($code -eq 0) { 'PASSED' } else { 'FAILED' }
@"
<!doctype html><meta charset="utf-8"><title>AI Stackprint test run</title>
<style>body{font:14px system-ui,sans-serif;margin:24px;background:#f6f6f7;color:#1f2328}h1{font-size:20px}
.s{padding:8px 12px;border-radius:6px;display:inline-block;background:$(if ($code -eq 0) { '#d6f0e0' } else { '#f9d6da' })}
table{border-collapse:collapse;width:100%;margin-top:16px}th{text-align:left;vertical-align:top;padding:8px;width:150px}td{padding:8px;width:45%}
img{max-width:100%;border:1px solid #ccc;border-radius:4px;background:#fff}td div{font-size:12px;color:#666}</style>
<h1>AI Stackprint: tests and screenshots</h1>
<p class="s">${status}: $passed passed, $failed failed, $skipped skipped. $($pngs.Count) screenshots. $(Get-Date -Format 'yyyy-MM-dd HH:mm')</p>
$failList
<table>$($rows -join "`n")</table>
"@ | Set-Content (Join-Path $Out 'index.html') -Encoding utf8

Write-Host "${status}: $passed passed, $failed failed, $skipped skipped, $($pngs.Count) screenshots"
Write-Host "Report: $(Join-Path $Out 'index.html')"
if ($Open) { Invoke-Item (Join-Path $Out 'index.html') }
exit $code
