#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Runs FHIR compatibility tests against the Ignixa API server.

.DESCRIPTION
    This script builds and starts Ignixa.Web, waits for its own listener to be ready,
    runs the compatibility tests using the CLI test runner, and then stops only that server.
    An occupied port is an error; an existing server is never stopped or reused.
    Requires PowerShell 7 or newer for owned process-tree cleanup.

.PARAMETER Filter
    Optional filter for test names (e.g., 'CreateTests', 'Metadata', 'SearchTests')

.PARAMETER Url
    Local loopback server URL (default: http://localhost:5000).
    Use the compatibility CLI directly to test an existing or remote server.

.PARAMETER Output
    Output JSON report file path, relative to the caller's directory unless absolute
    (default: compatibility-report.json).

.PARAMETER SkipBuild
    Skip building the projects before running

.PARAMETER KeepServerRunning
    Don't stop a successfully started API server after tests complete.
    Failed startup is always cleaned up.

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

function Get-PortListeners {
    param([int]$Port)
    Get-NetTCPConnection -ErrorAction Stop |
        Where-Object { $_.LocalPort -eq $Port -and $_.State -eq 'Listen' }
}

$apiDirectory = Join-Path $PSScriptRoot 'src\Application\Ignixa.Web'
$apiProject = Join-Path $apiDirectory 'Ignixa.Web.csproj'
$testProject = Join-Path $PSScriptRoot 'test\Ignixa.Tests.Compatibility.CLI\Ignixa.Tests.Compatibility.CLI.csproj'
$serverProcess = $null
$ready = $false
$testExitCode = 1

try {
    $outputProvider = $null
    $outputDrive = $null
    $Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $Output, [ref]$outputProvider, [ref]$outputDrive)
    if ($outputProvider.Name -ne 'FileSystem') {
        throw 'Output must resolve to a filesystem path.'
    }

    $serverUri = [System.Uri]$Url
    if (-not $serverUri.IsAbsoluteUri -or
        $serverUri.Scheme -notin @('http', 'https') -or
        -not $serverUri.IsLoopback -or $serverUri.Port -le 0 -or
        $serverUri.AbsolutePath -ne '/' -or $serverUri.Query -or
        $serverUri.Fragment -or $serverUri.UserInfo) {
        throw 'Url must be an absolute HTTP(S) loopback URL without a path, query, or credentials.'
    }
    $Url = $serverUri.AbsoluteUri.TrimEnd('/')
    $port = $serverUri.Port
    if (@(Get-PortListeners -Port $port).Count -gt 0) {
        throw "Port $port is already in use. Choose another port; existing processes will not be stopped."
    }
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

    $projectXml = [xml](Get-Content -LiteralPath $apiProject -Raw)
    $framework = $projectXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
    if (-not $framework -or [string]::IsNullOrWhiteSpace($framework.InnerText)) {
        throw 'Ignixa.Web must declare a single TargetFramework to locate its executable.'
    }
    $apiAssembly = Join-Path $apiDirectory "bin\Release\$($framework.InnerText)\Ignixa.Web.dll"
    if (-not (Test-Path -LiteralPath $apiAssembly -PathType Leaf)) {
        throw "Server assembly '$apiAssembly' does not exist. Run without -SkipBuild."
    }

    # Launch the managed executable directly: its PID, not a dotnet-run parent, owns the listener.
    $serverProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @("`"$apiAssembly`"", '--urls', "`"$Url`"") `
        -WorkingDirectory $apiDirectory `
        -PassThru `
        -NoNewWindow

    # Step 3: Wait for server to be ready
    Write-Step "Waiting for server to be ready..."
    $maxAttempts = 30
    $lastProbeError = 'No listener was created.'

    for ($attempt = 0; $attempt -lt $maxAttempts -and -not $ready; $attempt++) {
        if ($serverProcess.HasExited) {
            throw "Server exited with code $($serverProcess.ExitCode) before becoming ready."
        }
        $listeners = @(Get-PortListeners -Port $port)
        if (@($listeners | Where-Object { $_.OwningProcess -ne $serverProcess.Id }).Count -gt 0) {
            throw "Port $port was acquired by another process. Refusing to probe or test that server."
        }
        if ($listeners.Count -gt 0) {
            try {
                $response = Invoke-WebRequest -Uri "$Url/metadata" -Method GET -TimeoutSec 2 -ErrorAction Stop
                if ($response.StatusCode -eq 200 -and -not $serverProcess.HasExited) {
                    $ready = $true
                    Write-Success "Server is ready"
                } else {
                    $lastProbeError = "Metadata returned HTTP $($response.StatusCode)."
                }
            } catch {
                $lastProbeError = $_.Exception.Message
            }
        }
        if (-not $ready) {
            Write-Host "." -NoNewline
            Start-Sleep -Seconds 1
        }
    }

    if (-not $ready) {
        throw "Server did not become ready after $maxAttempts probes. $lastProbeError"
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
    if ((Test-Path -LiteralPath $Output) -and -not $Silent) {
        Write-Step "Launching test results viewer..."
        dotnet run --project $testProject --no-build --configuration Release -- viewer --report $Output
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMsg "Viewer failed (exit code: $LASTEXITCODE)"
            if ($testExitCode -eq 0) { $testExitCode = $LASTEXITCODE }
        }
    }

    # Step 5: Display results
    Write-Host ""
    if (Test-Path -LiteralPath $Output) {
        $report = Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json

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
    $testExitCode = 1
} finally {
    # Step 6: Cleanup
    if ($serverProcess) {
        try {
            if (-not $KeepServerRunning -or -not $ready) {
                Write-Step "Stopping owned API server (PID $($serverProcess.Id))..."
                if (-not $serverProcess.HasExited) {
                    try {
                        $serverProcess.Kill($true)
                    } catch [System.InvalidOperationException] {
                        if (-not $serverProcess.HasExited) { throw }
                    }
                    if (-not $serverProcess.WaitForExit(10000)) {
                        throw "Owned server PID $($serverProcess.Id) did not exit after termination."
                    }
                }
                Write-Success "Server stopped"
            } elseif (-not $serverProcess.HasExited) {
                Write-Step "Server is still running (KeepServerRunning parameter specified)"
                Write-Host "Server PID: $($serverProcess.Id)" -ForegroundColor Yellow
            }
        } catch {
            Write-ErrorMsg "Server cleanup failed: $_"
            $testExitCode = 1
        } finally {
            $serverProcess.Dispose()
        }
    }
}

exit $testExitCode
