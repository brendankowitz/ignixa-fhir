param(
    [string]$Runner = (Join-Path $PSScriptRoot '..\..\run-compat-tests.ps1'),
    [string]$Wrapper = (Join-Path $PSScriptRoot '..\..\run-compat-tests.cmd')
)

function Invoke-OwnedShell {
    param(
        [string]$File,
        [string[]]$Arguments,
        [hashtable]$Environment = @{}
    )
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ([IO.Path]::GetFileName($File) -ieq 'cmd.exe') {
        $start.Arguments = '/d /s /c "' + $Arguments[-1] + '"'
    } else {
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    }
    foreach ($name in $Environment.Keys) { $start.Environment[$name] = $Environment[$name] }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit(15000)) { throw 'Owned shell probe timed out.' }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        }
    } finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

Describe 'Compatibility runner shell prerequisites' {
    It 'rejects real Windows PowerShell 5 before any runner action' {
        $legacy = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $target = (Resolve-Path -LiteralPath $Runner).Path.Replace("'", "''")
        $bootstrap = @"
`$global:actions = 0
`$ProgressPreference = 'SilentlyContinue'
function global:dotnet { `$global:actions++; throw 'Unexpected dotnet call' }
function global:Start-Process { `$global:actions++; throw 'Unexpected process start' }
function global:Stop-Process { `$global:actions++; throw 'Unexpected process stop' }
function global:Get-NetTCPConnection { `$global:actions++; throw 'Unexpected TCP inspection' }
try { & '$target' -SkipBuild -Silent; `$code = `$LASTEXITCODE }
catch { [Console]::WriteLine("ErrorId=`$(`$_.FullyQualifiedErrorId)"); `$code = 1 }
[Console]::WriteLine("SideEffects=`$global:actions")
exit `$code
"@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($bootstrap))

        $result = Invoke-OwnedShell $legacy @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'ScriptRequiresUnmatchedPSVersion'
        $result.Output | Should Match 'SideEffects=0'
    }

    It 'fails clearly without pwsh and never falls back to Windows PowerShell' {
        $directory = Join-Path $TestDrive 'missing shell'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Copy-Item -LiteralPath $Wrapper -Destination (Join-Path $directory 'run-compat-tests.cmd')
        $marker = Join-Path $directory 'legacy-was-called.txt'
        [IO.File]::WriteAllText((Join-Path $directory 'powershell.cmd'),
            "@echo off`r`necho legacy>""$marker""`r`nexit /b 0`r`n")
        $command = '"' + (Join-Path $directory 'run-compat-tests.cmd') + '"'

        $result = Invoke-OwnedShell $env:ComSpec @('/d', '/c', $command) @{
            PATH = $directory
            PATHEXT = '.EXE;.COM;.BAT;.CMD'
        }

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'PowerShell 7'
        [IO.File]::Exists($marker) | Should Be $false
    }

    It 'uses real pwsh without execution-policy bypass and preserves the filter' {
        $directory = Join-Path $TestDrive 'supported shell'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Copy-Item -LiteralPath $Wrapper -Destination (Join-Path $directory 'run-compat-tests.cmd')
        $marker = Join-Path $directory 'invocation.json'
        $quotedMarker = $marker.Replace("'", "''")
        $probe = @"
param([string]`$Filter)
@{ Filter = `$Filter; CommandLine = [Environment]::CommandLine; Version = `$PSVersionTable.PSVersion.Major } |
    ConvertTo-Json | Set-Content -LiteralPath '$quotedMarker'
exit 17
"@
        [IO.File]::WriteAllText((Join-Path $directory 'run-compat-tests.ps1'), $probe)
        $command = '"' + (Join-Path $directory 'run-compat-tests.cmd') + '" "Create Tests"'

        $result = Invoke-OwnedShell $env:ComSpec @('/d', '/c', $command)

        $result.ExitCode | Should Be 17
        $invocation = [IO.File]::ReadAllText($marker) | ConvertFrom-Json
        $invocation.Filter | Should Be 'Create Tests'
        $invocation.Version | Should BeGreaterThan 6
        $invocation.CommandLine | Should Not Match 'ExecutionPolicy'
        $invocation.CommandLine | Should Not Match 'Bypass'
    }
}
