<#
.SYNOPSIS
  Runs all tests with code coverage and reports it: a summary, the classes with most uncovered lines, and an HTML report.
.DESCRIPTION
  Uses coverlet (build/coverage.runsettings says what counts: tests and generated code are left out) and reportgenerator
  (dotnet tool install -g dotnet-reportgenerator-globaltool). Output: artifacts/coverage/ (index.html, Summary.txt, Cobertura.xml).
  Fails (exit 1) when line coverage is under -Threshold, so it can gate a build.
.EXAMPLE
  pwsh build/coverage.ps1 -Threshold 95
#>
param(
    [int]$Threshold = 95,
    [int]$Top = 25
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'artifacts/coverage'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Push-Location $root
try {
    dotnet test --nologo -v q --settings build/coverage.runsettings --collect:'XPlat Code Coverage' --results-directory (Join-Path $out 'raw')
    $testCode = $LASTEXITCODE
    reportgenerator "-reports:$out/raw/**/coverage.cobertura.xml" "-targetdir:$out" '-reporttypes:Html;TextSummary;JsonSummary;Cobertura' | Out-Null
} finally { Pop-Location }

$summary = Get-Content (Join-Path $out 'Summary.json') -Raw | ConvertFrom-Json
$line = [double]$summary.summary.linecoverage
$branch = [double]$summary.summary.branchcoverage
Write-Host ""
Write-Host ("Line coverage {0}% ({1} of {2} lines), branch coverage {3}%" -f $line, $summary.summary.coveredlines, $summary.summary.coverablelines, $branch)
foreach ($a in $summary.coverage.assemblies) { Write-Host ("  {0}: {1}%" -f $a.name, $a.coverage) }
Write-Host "Classes with the most uncovered lines:"
$summary.coverage.assemblies.classesinassembly | ForEach-Object { [pscustomobject]@{ Class = $_.name; Uncovered = $_.coverablelines - $_.coveredlines; Percent = $_.coverage } } |
    Sort-Object Uncovered -Descending | Select-Object -First $Top | Format-Table -AutoSize
Write-Host "Report: $(Join-Path $out 'index.html')"
if ($testCode -ne 0) { Write-Host "Tests failed."; exit $testCode }
if ($line -lt $Threshold) { Write-Host "Line coverage $line% is under $Threshold%."; exit 1 }
