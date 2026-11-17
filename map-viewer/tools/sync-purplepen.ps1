<#
.SYNOPSIS
    Copies PurplePen source files verbatim into map-viewer/vendor/PurplePen.

.DESCRIPTION
    OCADVersioner vendors PurplePen's OCAD parser and Skia renderer as unmodified
    source. Project files are NOT vendored - map-viewer/src/*/ holds our own thin
    csprojs that compile these sources. That split is deliberate: the sources need
    no edits, the project files need many, so syncing stays a file copy rather than
    a merge.

    The manifest below is explicit on purpose. A new file appearing upstream should
    surface as a conscious decision, not be swept in by a glob.

.PARAMETER PurplePenPath
    Path to a PurplePen working copy.

.PARAMETER Ref
    Git ref to sync from. Defaults to the currently checked-out commit.

.PARAMETER Check
    Verify only: fail if the vendored tree differs from the source. Used in CI to
    catch accidental local edits to vendored files.

.EXAMPLE
    ./sync-purplepen.ps1 -PurplePenPath C:\repos\PurplePen
    ./sync-purplepen.ps1 -PurplePenPath C:\repos\PurplePen -Check
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PurplePenPath,
    [string] $Ref,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

$vendorRoot = Join-Path $PSScriptRoot '..\vendor\PurplePen'
$vendorRoot = [System.IO.Path]::GetFullPath($vendorRoot)
$srcRoot    = Join-Path ([System.IO.Path]::GetFullPath($PurplePenPath)) 'src'

if (-not (Test-Path $srcRoot)) { throw "Not a PurplePen working copy: $PurplePenPath" }

# Each entry: destination subdirectory, source directory, and file patterns.
# Patterns are non-recursive; nested directories get their own entry.
$manifest = @(
    @{ Dest = 'Graphics2D';            From = 'MapModel\Graphics2D';                       Files = @('*.cs') }
    @{ Dest = 'Graphics2D\Properties'; From = 'MapModel\Graphics2D\Properties';            Files = @('AssemblyInfo.cs') }

    @{ Dest = 'MapModel';              From = 'MapModel\MapModel';                         Files = @('*.cs') }
    @{ Dest = 'MapModel\Properties';   From = 'MapModel\MapModel\Properties';              Files = @('AssemblyInfo.cs') }
    @{ Dest = 'MapModel\DebugCode';    From = 'MapModel\MapModel\DebugCode';               Files = @('*.cs') }
    # AuthorityCodeToProj4.ds is an embedded resource looked up by hardcoded name.
    # The uncompressed .txt twin is NOT embedded and is deliberately excluded.
    @{ Dest = 'MapModel\Projections';  From = 'MapModel\MapModel\Projections';             Files = @('*.cs', 'AuthorityCodeToProj4.ds') }

    @{ Dest = 'Map_SkiaStd';           From = 'MapModel\Map_SkiaStd';                      Files = @('*.cs') }

    # DotSpatial.Projections: source + the ~640 KB of embedded data only.
    # Nad/ is 34 MB on disk but only null.lla is embedded - do not copy the rest.
    @{ Dest = 'DotSpatial.Projections';            From = 'DotSpatialProjectionsLite\DotSpatial.Projections';            Files = @('*.cs', '*.resx') }
    @{ Dest = 'DotSpatial.Projections\Transforms'; From = 'DotSpatialProjectionsLite\DotSpatial.Projections\Transforms'; Files = @('*.cs') }
    @{ Dest = 'DotSpatial.Projections\Reflection'; From = 'DotSpatialProjectionsLite\DotSpatial.Projections\Reflection'; Files = @('*.cs') }
    @{ Dest = 'DotSpatial.Projections\Properties'; From = 'DotSpatialProjectionsLite\DotSpatial.Projections\Properties'; Files = @('*.cs') }
    @{ Dest = 'DotSpatial.Projections\Nad';        From = 'DotSpatialProjectionsLite\DotSpatial.Projections\Nad';        Files = @('*.cs', 'null.lla') }
    @{ Dest = 'DotSpatial.Projections\XML';        From = 'DotSpatialProjectionsLite\DotSpatial.Projections\XML';        Files = @('gcs.csv', 'pcs.csv', 'datums.xml') }

    # Fonts: the browser has no system fonts, so these ship with the app.
    @{ Dest = 'fonts'; From = 'MapModel\Map_Skia.Tests\fonts'; Files = @(
        'Roboto-Regular.ttf', 'Roboto-Bold.ttf', 'Roboto-Italic.ttf', 'Roboto-BoldItalic.ttf',
        'RobotoCondensed-Regular.ttf', 'RobotoCondensed-Bold.ttf',
        'RobotoCondensed-Italic.ttf', 'RobotoCondensed-BoldItalic.ttf') }
)

# Resolve the upstream commit for provenance.
Push-Location $PurplePenPath
try {
    if (-not $Ref) { $Ref = 'HEAD' }
    $sha     = (& git rev-parse $Ref).Trim()
    $subject = (& git log -1 --format=%s $sha).Trim()
    $date    = (& git log -1 --format=%cI $sha).Trim()
} finally { Pop-Location }

Write-Host "PurplePen $sha  ($date)"
Write-Host "  $subject"

if (-not $Check) {
    if (Test-Path $vendorRoot) { Remove-Item $vendorRoot -Recurse -Force }
}

$records = @()
$copied  = 0
$drift   = @()

foreach ($entry in $manifest) {
    $fromDir = Join-Path $srcRoot $entry.From
    if (-not (Test-Path $fromDir)) { throw "Missing upstream directory: $fromDir" }
    $destDir = Join-Path $vendorRoot $entry.Dest

    $found = @()
    foreach ($pattern in $entry.Files) {
        $found += Get-ChildItem -Path $fromDir -Filter $pattern -File
    }
    $found = $found | Sort-Object Name -Unique
    if ($found.Count -eq 0) { throw "Manifest entry matched no files: $($entry.From)" }

    if (-not $Check -and -not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    foreach ($file in $found) {
        $target = Join-Path $destDir $file.Name
        if ($Check) {
            if (-not (Test-Path $target)) {
                $drift += "missing: $($entry.Dest)\$($file.Name)"
                continue
            }
            $a = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
            $b = (Get-FileHash $target        -Algorithm SHA256).Hash
            if ($a -ne $b) { $drift += "differs: $($entry.Dest)\$($file.Name)" }
            $records += [pscustomobject]@{ Path = "$($entry.Dest)/$($file.Name)"; Sha256 = $a; Bytes = $file.Length }
        }
        else {
            Copy-Item $file.FullName $target -Force
            $hash = (Get-FileHash $target -Algorithm SHA256).Hash
            $records += [pscustomobject]@{ Path = "$($entry.Dest)/$($file.Name)"; Sha256 = $hash; Bytes = $file.Length }
            $copied++
        }
    }
}

if ($Check) {
    if ($drift.Count -gt 0) {
        Write-Host ""
        Write-Host "Vendored tree does not match upstream $sha :" -ForegroundColor Red
        $drift | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "OK - $($records.Count) vendored files match upstream $sha."
    exit 0
}

$totalBytes = ($records | Measure-Object -Property Bytes -Sum).Sum
Write-Host "Copied $copied files ($([math]::Round($totalBytes / 1MB, 2)) MB)."

# Rewrite the provenance manifest.
$upstreamMd = Join-Path $vendorRoot '..\UPSTREAM.md'
$upstreamMd = [System.IO.Path]::GetFullPath($upstreamMd)

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# Vendored PurplePen sources')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Generated by `tools/sync-purplepen.ps1`. Do not edit these files by hand -')
[void]$sb.AppendLine('CI runs the script with `-Check` and fails if the tree has drifted.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| | |')
[void]$sb.AppendLine('|---|---|')
[void]$sb.AppendLine('| Upstream | https://github.com/petergolde/PurplePen.git |')
[void]$sb.AppendLine("| Commit | ``$sha`` |")
[void]$sb.AppendLine("| Commit date | $date |")
[void]$sb.AppendLine("| Subject | $subject |")
[void]$sb.AppendLine("| Synced | $(Get-Date -Format 'yyyy-MM-dd') |")
[void]$sb.AppendLine("| Files | $($records.Count) |")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Licences')
[void]$sb.AppendLine()
[void]$sb.AppendLine('- `Graphics2D`, `MapModel`, `Map_SkiaStd`, `fonts` - BSD 3-Clause, (c) 2006-2008 Peter Golde. See `LICENSE-PurplePen.txt`.')
[void]$sb.AppendLine('- `DotSpatial.Projections` - LGPL 2.1. See `LICENSE-DotSpatial.txt`.')
[void]$sb.AppendLine('- Roboto fonts - Apache 2.0. See `LICENSE-Roboto.txt`.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Patch log')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Deviations from verbatim upstream source. Keep this empty if at all possible;')
[void]$sb.AppendLine('every entry must be re-applied after a sync.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('_(none)_')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## File manifest')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| File | Bytes | SHA256 |')
[void]$sb.AppendLine('|---|---:|---|')
foreach ($r in ($records | Sort-Object Path)) {
    [void]$sb.AppendLine("| ``$($r.Path)`` | $($r.Bytes) | ``$($r.Sha256.ToLowerInvariant())`` |")
}

[System.IO.File]::WriteAllText($upstreamMd, $sb.ToString())
Write-Host "Wrote $upstreamMd"
