<#
.SYNOPSIS
  Moves the entries of PublicAPI.Unshipped.txt into PublicAPI.Shipped.txt in the four library projects (ADR 0028).
.DESCRIPTION
  Run it when a release is prepared (RELEASING.md). Entries marked *REMOVED* are taken out of the shipped file;
  all other entries are added to it. The shipped files stay sorted; the unshipped files are reset.
.PARAMETER Root
  The repository root; defaults to the parent of this script's folder.
#>
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$header = '#nullable enable'

foreach ($project in 'Core', 'Extensions', 'Imp', 'IO') {
    $folder = Join-Path $Root "src/SpatialNET.$project"
    $shippedPath = Join-Path $folder 'PublicAPI.Shipped.txt'
    $unshippedPath = Join-Path $folder 'PublicAPI.Unshipped.txt'

    $shipped = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in Get-Content $shippedPath -Encoding utf8) {
        if ($line.Trim() -ne '' -and $line -ne $header) { [void]$shipped.Add($line) }
    }

    $added = 0; $removed = 0
    foreach ($line in Get-Content $unshippedPath -Encoding utf8) {
        if ($line.Trim() -eq '' -or $line -eq $header) { continue }
        if ($line.StartsWith('*REMOVED*')) {
            if ($shipped.Remove($line.Substring('*REMOVED*'.Length))) { $removed++ }
            else { Write-Warning "$project`: $line is not in the shipped file." }
        }
        elseif ($shipped.Add($line)) { $added++ }
    }

    $sorted = [string[]]$shipped
    [System.Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    # UTF-8 without a byte order mark, like the files that the analyzer's code fix writes
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllLines($shippedPath, (@($header) + $sorted), $utf8)
    [System.IO.File]::WriteAllLines($unshippedPath, @($header), $utf8)
    Write-Host "SpatialNET.$project`: $added added, $removed removed."
}
