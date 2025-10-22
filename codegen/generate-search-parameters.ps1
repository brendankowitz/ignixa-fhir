#!/usr/bin/env pwsh
# Generate FHIR SearchParameter definitions
# This script uses fhir-codegen with our custom CSharpSearchParameterLanguage

param(
    [Parameter()]
    [ValidateSet('R4', 'R4B', 'R5', 'STU3', 'All')]
    [string]$FhirVersion = 'All'
)

$ErrorActionPreference = "Stop"

# Paths
$scriptDir = $PSScriptRoot
$outputDir = Join-Path $scriptDir ".." "src" "Ignixa.Search" "Generated"
$codegenExe = Join-Path $scriptDir "fhir-codegen" "src" "fhir-codegen" "bin" "Release" "net8.0" "fhir-codegen.exe"

# Create output directory
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "Building fhir-codegen tool..." -ForegroundColor Cyan
Push-Location (Join-Path $scriptDir "fhir-codegen")
try {
    dotnet build -c Release src/fhir-codegen/fhir-codegen.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build fhir-codegen"
    }
}
finally {
    Pop-Location
}

Write-Host "Building Ignixa.Specification.Generators..." -ForegroundColor Cyan
dotnet build -c Release (Join-Path $scriptDir "Ignixa.Specification.Generators" "Ignixa.Specification.Generators.csproj")
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build Sparky.Specification.Generators"
}

# Function to generate for a specific version
function Generate-Version {
    param([string]$Version)

    Write-Host "`nGenerating $Version search parameters..." -ForegroundColor Green

    $packageMap = @{
        'R4' = 'hl7.fhir.r4.core'
        'R4B' = 'hl7.fhir.r4b.core'
        'R5' = 'hl7.fhir.r5.core'
        'STU3' = 'hl7.fhir.r3.core'
    }

    $package = $packageMap[$Version]

    # Run fhir-codegen with our custom language
    & $codegenExe `
        --fhir-package $package `
        --language CSharpSearchParameter `
        --language-options "{`"OutputDirectory`":`"$outputDir`",`"Namespace`":`"Ignixa.Search.Generated`"}"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to generate $Version search parameters"
        return $false
    }

    Write-Host "✓ Generated $Version search parameters" -ForegroundColor Green
    return $true
}

# Generate requested versions
$versions = if ($FhirVersion -eq 'All') { @('R4', 'R4B', 'R5', 'STU3') } else { @($FhirVersion) }

$success = $true
foreach ($version in $versions) {
    if (-not (Generate-Version $version)) {
        $success = $false
    }
}

if ($success) {
    Write-Host "`n✓ All search parameters generated successfully!" -ForegroundColor Green
    Write-Host "Output directory: $outputDir" -ForegroundColor Cyan
} else {
    Write-Error "Some search parameters failed to generate"
    exit 1
}
