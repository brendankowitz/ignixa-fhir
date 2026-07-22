<#
.SYNOPSIS
Package ALT (Azure Load Testing) artifact zip for FHIR TestScript load testing.

.DESCRIPTION
Publishes the ignixa-matrix CLI as a self-contained Linux binary, bundles it with
TestScripts and Locust configuration, and creates a zip artifact suitable for
Azure Load Testing's 50 MB size limit.

.PARAMETER OutputPath
Path to write the output zip file. Defaults to "artifacts/alt-testscript-artifact.zip"
relative to the repository root.

.PARAMETER StagingDir
Temporary directory for build artifacts. Defaults to a system temp path.

.EXAMPLE
.\package-alt-artifact.ps1
# Creates artifacts/alt-testscript-artifact.zip with default settings

.\package-alt-artifact.ps1 -OutputPath "./my-artifact.zip"
# Creates ./my-artifact.zip

#>

param(
    [string]$OutputPath = "artifacts/alt-testscript-artifact.zip",
    [string]$StagingDir = $null
)

$ErrorActionPreference = 'Stop'

# Resolve script location and repo root
$scriptPath = Split-Path -Resolve $PSCommandPath
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptPath))

Write-Host "=== ALT Artifact Packaging ===" -ForegroundColor Cyan
Write-Host "Script location: $scriptPath"
Write-Host "Repo root: $repoRoot"

# Create staging directory if not provided
if (-not $StagingDir) {
    $StagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "alt-artifact-staging-$(Get-Random)"
}
Write-Host "Staging directory: $StagingDir"

# Clean and create staging
if (Test-Path $StagingDir) {
    Remove-Item $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDir | Out-Null

try {
    # Step 1: Publish ignixa-matrix CLI
    Write-Host "`n[1/3] Publishing ignixa-matrix CLI..." -ForegroundColor Yellow
    $cliPath = Join-Path $repoRoot "tools/Ignixa.ConformanceMatrix.Cli/Ignixa.ConformanceMatrix.Cli.csproj"
    $publishOutputPath = Join-Path $StagingDir "runner"

    if (-not (Test-Path $cliPath)) {
        throw "CLI project not found at: $cliPath"
    }

    # Use dotnet publish with release configuration for linux-x64
    & dotnet publish $cliPath `
        -c Release `
        -r linux-x64 `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:InvariantGlobalization=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishOutputPath `
        2>&1 | ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    # Symbols are useless on the ALT engine and eat into the 50 MB zip budget.
    Get-ChildItem $publishOutputPath -Filter *.pdb | Remove-Item -Force

    Write-Host "Published to: $publishOutputPath" -ForegroundColor Green

    # Step 2: Copy TestScripts
    Write-Host "`n[2/3] Copying TestScripts..." -ForegroundColor Yellow
    $testscriptsSource = Join-Path $repoRoot "src/Core/Ignixa.TestScript.Suites/testscripts"
    $testscriptsDest = Join-Path $StagingDir "testscripts"

    if (-not (Test-Path $testscriptsSource)) {
        throw "TestScripts source not found at: $testscriptsSource"
    }

    # Copy preserving directory structure
    Copy-Item -Path $testscriptsSource -Destination $testscriptsDest -Recurse -Force
    $scriptCount = (Get-ChildItem $testscriptsDest -Recurse -File).Count
    Write-Host "Copied $scriptCount TestScript files" -ForegroundColor Green

    # Deliberately NOT bundling locustfile.py / requirements.txt / locust.conf:
    # those upload to ALT as the test plan and configuration files, and ALT
    # auto-extracts the zip next to the test plan — a stale copy inside the zip
    # would clobber the uploaded locustfile.

    # Step 3: Create zip artifact
    Write-Host "`n[3/3] Creating zip artifact..." -ForegroundColor Yellow

    # Resolve output path (relative paths are anchored at the repo root)
    $resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
    $outputDir = Split-Path $resolvedOutputPath

    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir | Out-Null
    }

    # Remove existing zip if present
    if (Test-Path $resolvedOutputPath) {
        Remove-Item $resolvedOutputPath -Force
    }

    # Create zip using PowerShell's Compress-Archive
    Compress-Archive -Path (Join-Path $StagingDir "*") `
        -DestinationPath $resolvedOutputPath `
        -Force

    Write-Host "Created artifact: $resolvedOutputPath" -ForegroundColor Green

    # Report size
    $zipSize = (Get-Item $resolvedOutputPath).Length / 1MB
    Write-Host "`n=== Packaging Summary ===" -ForegroundColor Cyan
    Write-Host "Artifact size: $([Math]::Round($zipSize, 2)) MB"
    Write-Host "ALT limit: 50 MB per zip"

    if ($zipSize -gt 50) {
        Write-Host "WARNING: Artifact exceeds 50 MB limit! ALT will reject this file." -ForegroundColor Red
        Write-Host "Recommendations:"
        Write-Host "  - Split testscripts across multiple zips"
        Write-Host "  - Enable more aggressive compression in the dotnet publish step"
        Write-Host "  - Remove unused TestScript suites from the source"
    }
    else {
        $headroom = 50 - $zipSize
        Write-Host "Headroom: $([Math]::Round($headroom, 2)) MB" -ForegroundColor Green
    }

    Write-Host "Files: $($(Get-ChildItem $StagingDir -Recurse -File).Count) total" -ForegroundColor Green
    Write-Host "`nUpload to Azure Load Testing together with locustfile.py (test plan)," -ForegroundColor Green
    Write-Host "requirements.txt and locust.conf (configuration files) from this folder." -ForegroundColor Green

}
finally {
    # Clean up staging directory
    if (Test-Path $StagingDir) {
        Write-Host "`nCleaning up staging directory..." -ForegroundColor Gray
        Remove-Item $StagingDir -Recurse -Force
    }
}
