#!/usr/bin/env pwsh
# -------------------------------------------------------------------------------------------------
# Regen-drift guard for the typed-model generator.
#
# Regenerates the typed-model output and fails if it differs from the output already on disk.
# Catches: content drift in files the generator still emits, and classification churn (e.g. when a
# value-set gains/loses codes between versions and an element demotes from base to per-version --
# that changes WHICH files get emitted, which this guard also catches, since the newly-orphaned file
# keeps its stale content while its sibling per-version files appear as new/changed).
#
# Known gap: the generator only creates/overwrites, it never deletes. A file the CURRENT
# classification no longer produces at all is untouched by regeneration, so its content is identical
# before and after -> this guard reports "up to date" even though that file is orphaned dead code.
# It does NOT independently catch a type flipping OUT of a directory with nothing left behind to
# diff against; watch the generator's own console diff/emit-count output (or a plain `git status`
# after regen) for files that should have disappeared.
#
# It compares a content snapshot of the generated dirs taken BEFORE and AFTER regeneration, so it
# works whether or not the generated output is committed yet. In CI (where the output IS committed)
# this is equivalent to "regenerate, then assert no git diff".
#
# Run locally or in CI:  pwsh build/check-typed-model-regen.ps1
# Requires the FHIR packages (cached offline in this repo); does NOT hit the network in CI.
# -------------------------------------------------------------------------------------------------
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    $generatedDirs = @(
        'src/Core/Ignixa.Serialization/Generated/Models',
        'src/Core/Models/Ignixa.Models.R4/Generated',
        'src/Core/Models/Ignixa.Models.R5/Generated'
    )

    function Get-Snapshot {
        $entries = @()
        foreach ($dir in $generatedDirs) {
            $full = Join-Path $repoRoot $dir
            if (-not (Test-Path $full)) { continue }
            Get-ChildItem -Path $full -Recurse -File | Sort-Object FullName | ForEach-Object {
                $rel = [IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/')
                $hash = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash
                $entries += "$rel`:$hash"
            }
        }
        return ($entries -join "`n")
    }

    $before = Get-Snapshot

    Write-Host 'Regenerating typed-model output...' -ForegroundColor Cyan
    dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Generator failed with exit code $LASTEXITCODE."
        exit 1
    }

    $after = Get-Snapshot

    if ($before -eq $after) {
        Write-Host 'OK: generated typed-model output is up to date.' -ForegroundColor Green
        exit 0
    }

    Write-Host 'DRIFT: typed-model output changed after regeneration. Commit the regenerated files:' -ForegroundColor Red
    Write-Host '  dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model'
    Write-Host ''
    git --no-pager diff -- $generatedDirs
    git status --porcelain -- $generatedDirs
    exit 1
}
finally {
    Pop-Location
}
