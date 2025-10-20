#!/usr/bin/env pwsh
# Test script for CSharpInvariantLanguage code generator

param(
    [Parameter()]
    [ValidateSet('R4', 'R4B', 'R5', 'STU3')]
    [string]$FhirVersion = 'R4'
)

$ErrorActionPreference = "Stop"

# Paths
$scriptDir = $PSScriptRoot
$outputDir = Join-Path $scriptDir ".." "src" "Ignixa.Specification" "Generated"
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
    throw "Failed to build Ignixa.Specification.Generators"
}

Write-Host "`nGenerating $FhirVersion invariant provider..." -ForegroundColor Green

$packageMap = @{
    'R4' = 'hl7.fhir.r4.core'
    'R4B' = 'hl7.fhir.r4b.core'
    'R5' = 'hl7.fhir.r5.core'
    'STU3' = 'hl7.fhir.r3.core'
}

$package = $packageMap[$FhirVersion]

# Run fhir-codegen with CSharpInvariant language
Write-Host "Running fhir-codegen with package: $package" -ForegroundColor Cyan
& $codegenExe `
    --fhir-package $package `
    --language CSharpInvariant `
    --language-options "{`"OutputDirectory`":`"$outputDir`",`"Namespace`":`"Ignixa.Specification.Generated`"}"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to generate $FhirVersion invariant provider"
    exit 1
}

Write-Host ""
Write-Host "Generated $FhirVersion invariant provider successfully!" -ForegroundColor Green

# Check if file was created
$fileName = $FhirVersion + "InvariantProvider.g.cs"
$outputFile = Join-Path $outputDir $fileName
Write-Host "Output file: $outputFile" -ForegroundColor Cyan

if (Test-Path $outputFile) {
    $fileSize = (Get-Item $outputFile).Length
    Write-Host "File size: $fileSize bytes" -ForegroundColor Cyan

    # Count constraints (lines with "[" at start)
    $lines = Get-Content $outputFile
    $constraintLines = $lines | Where-Object { $_ -match '^\s+\[' }
    $constraintCount = ($constraintLines | Measure-Object).Count
    Write-Host "Constraint count: $constraintCount" -ForegroundColor Cyan
} else {
    Write-Host "ERROR: Output file was not created" -ForegroundColor Red
    exit 1
}
