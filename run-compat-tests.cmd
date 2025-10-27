@echo off
REM FHIR Compatibility Test Runner (Batch wrapper for PowerShell script)
REM
REM Usage:
REM   run-compat-tests.cmd                    - Run all tests
REM   run-compat-tests.cmd CreateTests        - Run tests matching "CreateTests"
REM   run-compat-tests.cmd SearchTests        - Run tests matching "SearchTests"
REM   run-compat-tests.cmd Metadata           - Run tests matching "Metadata"
REM
REM For more options, use PowerShell script directly:
REM   pwsh .\run-compat-tests.ps1 -Filter "CreateTests" -SkipBuild
REM   pwsh .\run-compat-tests.ps1 -KeepServerRunning
REM   pwsh .\run-compat-tests.ps1 -Output "my-report.json"

setlocal

REM Check if PowerShell is available
set PS_COMMAND=
pwsh -Version >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set PS_COMMAND=pwsh
) else (
    powershell -Version >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        set PS_COMMAND=powershell
    ) else (
        echo Error: PowerShell not found. Please install PowerShell.
        exit /b 1
    )
)

REM Build PowerShell command
if "%~1"=="" (
    %PS_COMMAND% -ExecutionPolicy Bypass -File "%~dp0run-compat-tests.ps1"
) else (
    %PS_COMMAND% -ExecutionPolicy Bypass -File "%~dp0run-compat-tests.ps1" -Filter "%~1"
)

exit /b %ERRORLEVEL%
