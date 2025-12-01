#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs FHIR compatibility tests against the Ignixa API server.

.DESCRIPTION
    This script starts the Ignixa.Api server, waits for it to be ready,
    runs the compatibility tests using the CLI test runner, and then stops the server.

.PARAMETER Filter
    Optional filter for test names (e.g., 'CreateTests', 'Metadata', 'SearchTests')

.PARAMETER Url
    Server URL to test against (default: http://localhost:5000)

.PARAMETER Output
    Output JSON report file path (default: compatibility-report.json)

.PARAMETER SkipBuild
    Skip building the projects before running

.PARAMETER KeepServerRunning
    Don't stop the API server after tests complete

.PARAMETER Silent
    Don't launch the viewer after tests complete

.EXAMPLE
    .\run-compat-tests.ps1
    Run all compatibility tests and launch viewer

.EXAMPLE
    .\run-compat-tests.ps1 -Filter "CreateTests"
    Run only tests matching "CreateTests"

.EXAMPLE
    .\run-compat-tests.ps1 -SkipBuild
    Run tests without rebuilding

.EXAMPLE
    .\run-compat-tests.ps1 -Silent
    Run tests without launching the viewer

.EXAMPLE
    .\run-compat-tests.ps1 -KeepServerRunning
    Keep server running after tests for debugging
#>

param(
    [string]$Filter = "",
    [string]$Url = "http://localhost:5000",
    [string]$Output = "compatibility-report.json",
    [switch]$SkipBuild,
    [switch]$KeepServerRunning,
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-ErrorMsg {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

$apiProject = "src/Application/Ignixa.Api/Ignixa.Api.csproj"
$testProject = "test/Ignixa.Tests.Compatibility.CLI/Ignixa.Tests.Compatibility.CLI.csproj"
$serverProcess = $null

try {
    # Step 1: Build projects (unless skipped)
    if (-not $SkipBuild) {
        Write-Step "Building API and test projects..."
        dotnet build $apiProject --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "API build failed" }

        dotnet build $testProject --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Test project build failed" }

        Write-Success "Build completed"
    } else {
        Write-Step "Skipping build (SkipBuild parameter specified)"
    }

    # Step 2: Start API server in background
    Write-Step "Starting API server at $Url..."

    # Kill any existing processes on port 5000
    $port = ([System.Uri]$Url).Port
    $existingProcess = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    if ($existingProcess) {
        Write-Step "Stopping existing process on port $port..."
        Stop-Process -Id $existingProcess -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    $serverProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "run --project $apiProject --no-build --configuration Release --urls $Url" `
        -PassThru `
        -NoNewWindow

    # Step 3: Wait for server to be ready
    Write-Step "Waiting for server to be ready..."
    $maxAttempts = 30
    $attempt = 0
    $ready = $false

    while ($attempt -lt $maxAttempts -and -not $ready) {
        try {
            $response = Invoke-WebRequest -Uri "$Url/metadata" -Method GET -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                $ready = $true
                Write-Success "Server is ready"
            }
        } catch {
            $attempt++
            Write-Host "." -NoNewline
            Start-Sleep -Seconds 1
        }
    }

    if (-not $ready) {
        throw "Server failed to start within 30 seconds"
    }

    Write-Host ""

    # Step 4: Run compatibility tests
    Write-Step "Running compatibility tests..."

    $testArgs = @(
        "run",
        "--project", $testProject,
        "--no-build",
        "--configuration", "Release",
        "--",
        "--url", $Url,
        "--output", $Output
    )

    if ($Filter) {
        $testArgs += "--filter"
        $testArgs += $Filter
        Write-Step "Filter: $Filter"
    }

    dotnet @testArgs
    $testExitCode = $LASTEXITCODE

    # Launch viewer after tests complete (unless Silent flag is set)
    if ((Test-Path $Output) -and -not $Silent) {
        Write-Step "Launching test results viewer..."
        dotnet run --project $testProject --no-build --configuration Release -- viewer
    }

    # Step 5: Display results
    Write-Host ""
    if (Test-Path $Output) {
        $report = Get-Content $Output | ConvertFrom-Json

        Write-Host "=== Test Results ===" -ForegroundColor Cyan
        Write-Host "Server: $($report.ServerUrl)"
        Write-Host "Total Tests: $($report.TotalTests)"

        $passPercent = [math]::Round($report.PassRate * 100, 1)
        $passText = "Passed: $($report.Passed) ($passPercent percent)"
        Write-Host $passText -ForegroundColor Green

        $failColor = if ($report.Failed -gt 0) { "Red" } else { "Gray" }
        Write-Host "Failed: $($report.Failed)" -ForegroundColor $failColor
        Write-Host "Skipped: $($report.Skipped)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Report saved to: $Output" -ForegroundColor Cyan
    }

    if ($testExitCode -eq 0) {
        Write-Success "All tests completed successfully"
    } else {
        Write-ErrorMsg "Tests completed with errors (exit code: $testExitCode)"
    }

} catch {
    Write-ErrorMsg "Error: $_"
    exit 1
} finally {
    # Step 6: Cleanup
    if ($serverProcess -and -not $KeepServerRunning) {
        Write-Step "Stopping API server..."
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Success "Server stopped"
    } elseif ($KeepServerRunning) {
        Write-Step "Server is still running (KeepServerRunning parameter specified)"
        $pidText = "Server PID: $($serverProcess.Id)"
        Write-Host $pidText -ForegroundColor Yellow
    }
}

exit $testExitCode
