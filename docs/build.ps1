<#
.SYNOPSIS
  Builds the documentation sites (English and German) with DocFX into docs/_site.
.PARAMETER Serve
  Serves the result at http://localhost:8080 after the build.
#>
param([switch]$Serve)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }

    # The English site generates the API reference from the XML comments; the German site links to it.
    foreach ($lang in 'en', 'de') {
        dotnet docfx "docfx.$lang.json" --warningsAsErrors
        if ($LASTEXITCODE -ne 0) { throw "docfx build ($lang) failed" }
    }

    Set-Content -Path '_site/index.html' -Encoding utf8 -Value '<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="0; url=en/index.html"><a href="en/index.html">SpatialNET documentation</a>'

    if ($Serve) { dotnet docfx serve _site -p 8080 }
}
finally {
    Pop-Location
}
