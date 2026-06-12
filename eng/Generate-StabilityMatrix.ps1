<#
.SYNOPSIS
Generates docs/site/docs/core-sdk/stability.md from PackageStability properties (ADR 2606).

.DESCRIPTION
Scans the public package set (src/Core, tools, Ignixa.Sidecar.Contracts) and emits the
package stability matrix. Run from the repo root, or rely on the script locating the
repo root relative to its own path. CI regenerates this file in the docs workflow, so
the committed copy never has to be maintained by hand.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputPath = Join-Path $repoRoot 'docs/site/docs/core-sdk/stability.md'

$scanPaths = @(
    (Join-Path $repoRoot 'src/Core'),
    (Join-Path $repoRoot 'tools'),
    (Join-Path $repoRoot 'src/Application/Ignixa.Sidecar.Contracts')
)

$rank = @{ stable = 0; beta = 1; alpha = 2 }

$packages = $scanPaths |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-ChildItem $_ -Recurse -Filter '*.csproj' } |
    ForEach-Object {
        [xml]$xml = Get-Content -Raw $_.FullName
        $props = $xml.Project.PropertyGroup | ForEach-Object { $_ }
        $isPackable = ($props.IsPackable | Where-Object { $_ } | Select-Object -First 1)
        if ("$isPackable".Trim() -eq 'false') { return }

        $stability = ("$($props.PackageStability | Where-Object { $_ } | Select-Object -First 1)").Trim()
        if (-not $stability) { $stability = 'alpha' }
        $packageId = ("$($props.PackageId | Where-Object { $_ } | Select-Object -First 1)").Trim()
        if (-not $packageId) { $packageId = $_.BaseName }
        $description = ("$($props.Description | Where-Object { $_ } | Select-Object -First 1)").Trim()
        $isTool = ("$($props.PackAsTool | Where-Object { $_ } | Select-Object -First 1)").Trim() -eq 'true'

        [pscustomobject]@{
            PackageId   = $packageId
            Stability   = $stability.ToLowerInvariant()
            Description = $description
            IsTool      = $isTool
        }
    } |
    Sort-Object { $rank[$_.Stability] }, PackageId

function Format-Rows($items) {
    $items | ForEach-Object {
        $badge = switch ($_.Stability) {
            'stable' { 'Stable' }
            'beta'   { 'Beta (pre-release)' }
            'alpha'  { 'Alpha (experimental)' }
        }
        "| ``$($_.PackageId)`` | $badge | $($_.Description) |"
    }
}

$libraries = $packages | Where-Object { -not $_.IsTool }
$tools = $packages | Where-Object { $_.IsTool }

$content = @(
    '---'
    'sidebar_position: 2'
    'title: Package Stability'
    '---'
    ''
    '<!-- GENERATED FILE - do not edit by hand. -->'
    '<!-- Regenerate with: pwsh eng/Generate-StabilityMatrix.ps1 (also runs in the docs CI workflow). -->'
    ''
    '# Package Stability'
    ''
    'Ignixa packages are classified per [ADR 2606](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2606-nuget-experimental-versioning.md):'
    ''
    '| Level | Version format | Meaning |'
    '|-------|---------------|---------|'
    '| **Stable** | `1.0.0` | Production-ready, stable API |'
    '| **Beta** | `1.0.0-beta` | Feature-complete, API stabilizing |'
    '| **Alpha** | `1.0.0-alpha` | Experimental, breaking changes expected |'
    ''
    'Pre-release packages require the `--prerelease` flag (`dotnet add package <id> --prerelease`).'
    'A package is never more stable than any package it depends on.'
    ''
    '## Libraries'
    ''
    '| Package | Stability | Description |'
    '|---------|-----------|-------------|'
    (Format-Rows $libraries)
    ''
    '## CLI Tools'
    ''
    '| Package | Stability | Description |'
    '|---------|-----------|-------------|'
    (Format-Rows $tools)
    ''
) | ForEach-Object { $_ }

[System.IO.File]::WriteAllText($outputPath, (($content -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $outputPath ($($libraries.Count) libraries, $($tools.Count) tools)"
