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

REM Require PowerShell 7; never use the incompatible Windows PowerShell process APIs.
pwsh.exe -NoLogo -NoProfile -NonInteractive -Command "if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo Error: PowerShell 7 or newer ^(pwsh.exe^) is required. Install it and add it to PATH. >&2
    exit /b 1
)

REM Build PowerShell command
if "%~1"=="" (
    pwsh.exe -NoLogo -NoProfile -File "%~dp0run-compat-tests.ps1"
) else (
    pwsh.exe -NoLogo -NoProfile -File "%~dp0run-compat-tests.ps1" -Filter "%~1"
)

exit /b %ERRORLEVEL%
