param(
    [string]$ScriptUnderTest = (Join-Path $PSScriptRoot '..\..\run-compat-tests.ps1')
)

function dotnet { throw 'Native dotnet is disabled during compatibility script tests.' }

Describe 'Compatibility runner process ownership' {
    BeforeEach {
        if ($PSVersionTable.PSVersion.Major -lt 7 -or
            -not [Diagnostics.Process].GetMethod('Kill', [type[]]@([bool]))) {
            throw 'These runner tests require the real PowerShell 7 process-tree API.'
        }
        $global:IgnixaCompatTest = @{
            started = $false
            connectionMode = 'normal'
            probeCount = 0
            testExitCode = 0
            reportExists = $false
            dotnetCalls = [System.Collections.Generic.List[object]]::new()
            startArguments = $null
            startDirectory = $null
            messages = [System.Collections.Generic.List[string]]::new()
        }
        $global:IgnixaCompatTest.ownedProcess = [pscustomobject]@{
            Id = 73731
            HasExited = $false
            ExitCode = 23
            KillCount = 0
            Disposed = $false
        }
        $global:IgnixaCompatTest.ownedProcess | Add-Member ScriptMethod Kill {
            param([bool]$EntireProcessTree)
            $this.KillCount++
            $this.HasExited = $true
        }
        $global:IgnixaCompatTest.ownedProcess | Add-Member ScriptMethod WaitForExit {
            param([int]$Milliseconds)
            return $this.HasExited
        }
        $global:IgnixaCompatTest.ownedProcess | Add-Member ScriptMethod Dispose { $this.Disposed = $true }

        Mock Get-NetTCPConnection {
            if ($global:IgnixaCompatTest.connectionMode -eq 'occupied' -or
                ($global:IgnixaCompatTest.connectionMode -eq 'race' -and $global:IgnixaCompatTest.started)) {
                [pscustomobject]@{ LocalPort = 65101; State = 'Listen'; OwningProcess = 91919 }
            } elseif ($global:IgnixaCompatTest.started) {
                [pscustomobject]@{ LocalPort = 65101; State = 'Listen'; OwningProcess = $global:IgnixaCompatTest.ownedProcess.Id }
            }
        }
        Mock Start-Process {
            $global:IgnixaCompatTest.started = $true
            $global:IgnixaCompatTest.startArguments = $ArgumentList
            $global:IgnixaCompatTest.startDirectory = $WorkingDirectory
            $global:IgnixaCompatTest.ownedProcess
        }
        Mock Stop-Process {}
        Mock Start-Sleep {}
        Mock Invoke-WebRequest {
            $global:IgnixaCompatTest.probeCount++
            [pscustomobject]@{ StatusCode = 200 }
        }
        Mock Test-Path {
            ($LiteralPath -like '*.dll') -or ($Path -like '*.dll') -or $global:IgnixaCompatTest.reportExists
        }
        Mock Get-Content {
            '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        } -ParameterFilter { ($LiteralPath -like '*.csproj') -or ($Path -like '*.csproj') }
        Mock Get-Content {
            '{"ServerUrl":"http://localhost:65101","TotalTests":1,"Passed":1,"Failed":0,"Skipped":0,"PassRate":1}'
        } -ParameterFilter { ($LiteralPath -like '*.json') -or ($Path -like '*.json') }
        Mock dotnet {
            $global:IgnixaCompatTest.dotnetCalls.Add(@($args))
            $global:LASTEXITCODE = if ($args -contains '--url') { $global:IgnixaCompatTest.testExitCode } else { 0 }
        }
        Mock Write-Host { $global:IgnixaCompatTest.messages.Add([string]$Object) }

        # Never execute the destructive baseline or native dotnet if a mock was not installed.
        foreach ($command in @('Start-Process', 'Stop-Process', 'dotnet', 'Invoke-WebRequest')) {
            (Get-Command $command).CommandType | Should Be 'Function'
        }
    }

    AfterEach {
        Remove-Variable IgnixaCompatTest -Scope Global
    }

    It 'rejects an occupied port before building, starting, or stopping anything' {
        $global:IgnixaCompatTest.connectionMode = 'occupied'

        & $ScriptUnderTest -Url 'http://localhost:65101' -Silent

        $LASTEXITCODE | Should Be 1
        Assert-MockCalled dotnet -Times 0 -Exactly -Scope It
        Assert-MockCalled Start-Process -Times 0 -Exactly -Scope It
        Assert-MockCalled Stop-Process -Times 0 -Exactly -Scope It
        ($global:IgnixaCompatTest.messages -join "`n") | Should Match 'already in use'
    }

    It 'runs the Web assembly directly and preserves filter, output, and SkipBuild flags' {
        & $ScriptUnderTest -Url 'http://localhost:65101' -Filter 'Create Tests' -Output 'result report.json' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 0
        ($global:IgnixaCompatTest.startArguments -join ' ') | Should Match 'Ignixa\.Web\.dll'
        ($global:IgnixaCompatTest.startArguments -join ' ') | Should Not Match 'run --project'
        $global:IgnixaCompatTest.startArguments[0].StartsWith('"') | Should Be $true
        $global:IgnixaCompatTest.startArguments[0].EndsWith('"') | Should Be $true
        $global:IgnixaCompatTest.startDirectory | Should Match 'src\\Application\\Ignixa\.Web$'
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 1
        ($global:IgnixaCompatTest.dotnetCalls[0] -contains 'Create Tests') | Should Be $true
        ($global:IgnixaCompatTest.dotnetCalls[0] -contains (Join-Path (Get-Location).Path 'result report.json')) | Should Be $true
        ($global:IgnixaCompatTest.dotnetCalls[0] -contains '--no-build') | Should Be $true
        $global:IgnixaCompatTest.ownedProcess.KillCount | Should Be 1
        $global:IgnixaCompatTest.ownedProcess.Disposed | Should Be $true
        Assert-MockCalled Stop-Process -Times 0 -Exactly -Scope It
    }

    It 'builds the runnable Web project and the compatibility CLI in Release' {
        & $ScriptUnderTest -Url 'http://localhost:65101' -Silent

        $LASTEXITCODE | Should Be 0
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 3
        $global:IgnixaCompatTest.dotnetCalls[0][0] | Should Be 'build'
        $global:IgnixaCompatTest.dotnetCalls[0][1] | Should Match 'Ignixa\.Web\\Ignixa\.Web\.csproj$'
        ($global:IgnixaCompatTest.dotnetCalls[0] -contains 'Release') | Should Be $true
        $global:IgnixaCompatTest.dotnetCalls[1][1] | Should Match 'Ignixa\.Tests\.Compatibility\.CLI\.csproj$'
    }

    It 'terminates an actual owned PowerShell 7 process through the supported cleanup API' {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
        $start.UseShellExecute = $false
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 30')) {
            $start.ArgumentList.Add($argument)
        }
        $owned = [Diagnostics.Process]::Start($start)
        $monitor = [Diagnostics.Process]::GetProcessById($owned.Id)
        $null = $monitor.Handle
        $global:IgnixaCompatTest.ownedProcess = $owned
        try {
            & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent

            $LASTEXITCODE | Should Be 0
            $monitor.WaitForExit(5000) | Should Be $true
            Assert-MockCalled Stop-Process -Times 0 -Exactly -Scope It
        } finally {
            if (-not $monitor.HasExited) {
                $monitor.Kill($true)
                $monitor.WaitForExit()
            }
            $monitor.Dispose()
            $owned.Dispose()
        }
    }

    It 'resolves relative output against the caller filesystem location rather than process CWD' {
        $callerDirectory = Join-Path $TestDrive 'caller working directory'
        [IO.Directory]::CreateDirectory($callerDirectory) | Out-Null
        $originalLocation = Get-Location
        try {
            Set-Location -LiteralPath $callerDirectory
            [Environment]::CurrentDirectory | Should Not Be $callerDirectory
            $expected = Join-Path $callerDirectory 'caller report.json'

            & $ScriptUnderTest -Url 'http://localhost:65101' -Output 'caller report.json' -SkipBuild -Silent

            $LASTEXITCODE | Should Be 0
            ($global:IgnixaCompatTest.dotnetCalls[0] -contains $expected) | Should Be $true
        } finally {
            Set-Location -LiteralPath $originalLocation.Path
        }
    }

    It 'rejects a non-filesystem output before inspecting ports or starting work' {
        & $ScriptUnderTest -Url 'http://localhost:65101' -Output 'Env:PATH' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 1
        Assert-MockCalled Get-NetTCPConnection -Times 0 -Exactly -Scope It
        Assert-MockCalled dotnet -Times 0 -Exactly -Scope It
        Assert-MockCalled Start-Process -Times 0 -Exactly -Scope It
    }

    It 'does not probe or test a foreign listener that wins the startup race' {
        $global:IgnixaCompatTest.connectionMode = 'race'

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 1
        $global:IgnixaCompatTest.probeCount | Should Be 0
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 0
        $global:IgnixaCompatTest.ownedProcess.KillCount | Should Be 1
        Assert-MockCalled Stop-Process -Times 0 -Exactly -Scope It
    }

    It 'fails immediately when its server exits instead of accepting an unrelated response' {
        $global:IgnixaCompatTest.ownedProcess.HasExited = $true

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 1
        $global:IgnixaCompatTest.probeCount | Should Be 0
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 0
        $global:IgnixaCompatTest.ownedProcess.KillCount | Should Be 0
    }

    It 'bounds non-ready HTTP responses and cleans failed startup even with KeepServerRunning' {
        Mock Invoke-WebRequest {
            $global:IgnixaCompatTest.probeCount++
            if ($global:IgnixaCompatTest.probeCount -gt 31) { throw 'Baseline loop safety bound' }
            [pscustomobject]@{ StatusCode = 202 }
        }

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent -KeepServerRunning

        $LASTEXITCODE | Should Be 1
        $global:IgnixaCompatTest.probeCount | Should Be 30
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 0
        $global:IgnixaCompatTest.ownedProcess.KillCount | Should Be 1
    }

    It 'preserves the compatibility runner failure exit code' {
        $global:IgnixaCompatTest.testExitCode = 7

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 7
    }

    It 'reports cleanup failure instead of returning a successful test exit code' {
        $global:IgnixaCompatTest.ownedProcess | Add-Member ScriptMethod Kill {
            param([bool]$EntireProcessTree)
            throw [System.InvalidOperationException]::new('Owned process termination failed.')
        } -Force

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent

        $LASTEXITCODE | Should Be 1
        ($global:IgnixaCompatTest.messages -join "`n") | Should Match 'cleanup failed'
        $global:IgnixaCompatTest.ownedProcess.Disposed | Should Be $true
    }

    It 'keeps only its successfully started server when requested' {
        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent -KeepServerRunning

        $LASTEXITCODE | Should Be 0
        $global:IgnixaCompatTest.ownedProcess.KillCount | Should Be 0
        Assert-MockCalled Stop-Process -Times 0 -Exactly -Scope It
        ($global:IgnixaCompatTest.messages -join "`n") | Should Match '73731'
    }

    It 'honors Silent and launches the viewer only when requested' {
        $global:IgnixaCompatTest.reportExists = $true

        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild -Silent
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 1

        $global:IgnixaCompatTest.started = $false
        $global:IgnixaCompatTest.ownedProcess.HasExited = $false
        & $ScriptUnderTest -Url 'http://localhost:65101' -SkipBuild
        $global:IgnixaCompatTest.dotnetCalls.Count | Should Be 3
        ($global:IgnixaCompatTest.dotnetCalls[2] -contains 'viewer') | Should Be $true
        ($global:IgnixaCompatTest.dotnetCalls[2] -contains '--report') | Should Be $true
    }
}
