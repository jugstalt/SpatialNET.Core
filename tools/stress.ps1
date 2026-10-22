<#
.SYNOPSIS
  Runs the stress tests of SpatialNET: many random, deliberately dirty inputs per algorithm (see docs/adr/0008).

.PARAMETER Seconds
  Time budget per stress test (default 60).

.PARAMETER Iterations
  Upper limit of random cases per stress test (default 20000; the time budget usually ends first).

.PARAMETER Filter
  Restricts the run to tests whose name contains the text, for example "Overlays" or "Noder".

.NOTES
  A failing case prints a CsCheck seed. Replay it with
    $env:CsCheck_Seed = '<seed>'; ./tools/stress.ps1 -Filter <test name>
#>
param(
    [int] $Seconds = 60,
    [int] $Iterations = 20000,
    [string] $Filter = ''
)

$ErrorActionPreference = 'Stop'
$env:SPATIALNET_STRESS = '1'
$env:SPATIALNET_STRESS_SECONDS = "$Seconds"
$env:SPATIALNET_STRESS_ITER = "$Iterations"

$selection = 'Category=Stress'
if ($Filter -ne '') {
    $selection = "Category=Stress&FullyQualifiedName~$Filter"
}

dotnet test (Join-Path $PSScriptRoot '..' 'tests' 'SpatialNET.Tests') -c Release --filter $selection
exit $LASTEXITCODE
