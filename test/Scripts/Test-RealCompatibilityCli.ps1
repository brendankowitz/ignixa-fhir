param(
    [string]$CliDirectory = (Join-Path $PSScriptRoot '..\Ignixa.Tests.Compatibility.CLI\bin\Debug\net10.0'),
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$CliDirectory = (Resolve-Path -LiteralPath $CliDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$scratch = Join-Path $PSScriptRoot "bin\real-cli-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch -Force | Out-Null

function Invoke-CliProbe {
    param(
        [string]$Directory,
        [string]$Name,
        [string[]]$Arguments,
        [System.Net.Sockets.TcpListener]$FailureServer
    )

    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $Directory 'fhir-compat.exe'))
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $requests = 0
    try {
        while (-not $process.WaitForExit(50)) {
            if ($timer.Elapsed.TotalSeconds -gt 120) { throw "CLI probe '$Name' timed out." }
            if ($FailureServer -and $FailureServer.Pending()) {
                $client = $FailureServer.AcceptTcpClient()
                try {
                    $response = [Text.Encoding]::ASCII.GetBytes(
                        "HTTP/1.1 503 Service Unavailable`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    $client.GetStream().Write($response, 0, $response.Length)
                    $requests++
                } finally {
                    $client.Dispose()
                }
            }
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $output = $output -replace '\x1b\[[0-9;]*m', ''
        $output | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Name.log")
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Requests = $requests }
    } finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

try {
    $oracle = Join-Path $CliDirectory 'Microsoft.Health.Fhir.R4.Tests.E2E.dll'
    if (-not (Test-Path -LiteralPath $oracle -PathType Leaf)) {
        throw 'Build the real compatibility CLI with the legitimate upstream package before running these probes.'
    }
    $oracleHash = (Get-FileHash -LiteralPath $oracle).Hash
    "Upstream oracle SHA256: $oracleHash" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'oracle-provenance.log')

    $help = Invoke-CliProbe -Directory $CliDirectory -Name 'help' -Arguments @('--help')
    if ($help.ExitCode -ne 0 -or $help.Output -notmatch '--filter') { throw 'Real CLI help contract failed.' }

    $emptyPath = Join-Path $OutputDirectory 'empty-selection.json'
    $empty = Invoke-CliProbe -Directory $CliDirectory -Name 'empty-selection' -Arguments @(
        '--url', 'http://127.0.0.1:1',
        '--output', $emptyPath,
        '--filter', 'NoMatchingCompatibilityContract_79906c9453d94b849185'
    )
    $emptyReport = Get-Content -LiteralPath $emptyPath -Raw | ConvertFrom-Json
    if ($empty.ExitCode -eq 0 -or $emptyReport.TotalTests -ne 0 -or
        $empty.Output -notmatch '([1-9][0-9]*) test cases discovered; 0 selected') {
        throw 'Real upstream discovery/empty-selection exit contract failed.'
    }

    $missingDirectory = Join-Path $scratch 'missing-assembly'
    New-Item -ItemType Directory -Path $missingDirectory -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $CliDirectory) {
        if ($entry.Name -eq 'Microsoft.Health.Fhir.R4.Tests.E2E.dll') { continue }
        Copy-Item -LiteralPath $entry.FullName -Destination $missingDirectory -Recurse
    }
    $missing = Invoke-CliProbe -Directory $missingDirectory -Name 'missing-assembly' -Arguments @('--filter', 'MetadataTests')
    if ($missing.ExitCode -eq 0 -or $missing.Output -notmatch 'assembly not found') {
        throw 'Real CLI missing-assembly exit contract failed.'
    }

    $failureServer = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $failureServer.Start()
    try {
        $failurePath = Join-Path $OutputDirectory 'required-failure.json'
        $failure = Invoke-CliProbe -Directory $CliDirectory -Name 'required-failure' -FailureServer $failureServer -Arguments @(
            '--url', "http://127.0.0.1:$($failureServer.LocalEndpoint.Port)",
            '--output', $failurePath,
            '--filter', 'GivenInvalidFormatParameter_WhenGettingMetadata_TheServerShouldReturnNotAcceptable'
        )
        $failureReport = Get-Content -LiteralPath $failurePath -Raw | ConvertFrom-Json
        if ($failure.ExitCode -eq 0 -or $failureReport.TotalTests -ne 1 -or
            $failureReport.Failed -ne 1 -or $failure.Requests -eq 0) {
            throw 'Real upstream required-failure exit contract failed or no request reached the owned failure endpoint.'
        }
    } finally {
        $failureServer.Stop()
    }

    if ((Get-FileHash -LiteralPath $oracle).Hash -ne $oracleHash) { throw 'Upstream oracle was modified.' }
    [pscustomobject]@{
        HelpExitCode = $help.ExitCode
        EmptySelectionExitCode = $empty.ExitCode
        MissingAssemblyExitCode = $missing.ExitCode
        RequiredFailureExitCode = $failure.ExitCode
        RequiredTestsExecuted = $failureReport.TotalTests
        FailureEndpointRequests = $failure.Requests
        OracleSha256 = $oracleHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
    Write-Output 'PASS: four real CLI process contracts; one real upstream test executed; oracle unchanged.'
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
