<#
.SYNOPSIS
  Fails if the code coverage of the libraries falls below the thresholds.
.DESCRIPTION
  Reads the Cobertura report that `dotnet test --settings tests/coverage.runsettings` writes below
  -ResultsDirectory. Measured at introduction: lines 96.7 %, branches 94.6 %; the thresholds sit a little below.
#>
param(
    [string]$ResultsDirectory = 'TestResults',
    [double]$MinLine = 0.95,
    [double]$MinBranch = 0.90
)

$ErrorActionPreference = 'Stop'
$report = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if (-not $report) { throw "No coverage.cobertura.xml found below '$ResultsDirectory'." }

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$coverage = ([xml](Get-Content -Raw $report.FullName)).coverage
$line = [double]::Parse($coverage.'line-rate', $culture)
$branch = [double]::Parse($coverage.'branch-rate', $culture)

'{0}: lines {1:P1} (minimum {2:P0}), branches {3:P1} (minimum {4:P0})' -f $report.Name, $line, $MinLine, $branch, $MinBranch

if ($line -lt $MinLine -or $branch -lt $MinBranch) {
    throw 'Code coverage is below the threshold.'
}
