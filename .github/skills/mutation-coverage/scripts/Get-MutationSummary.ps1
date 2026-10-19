<#
.SYNOPSIS
    Summarizes a Stryker.NET mutation-report.json for test-suite curation.

.DESCRIPTION
    Reports surviving and uncovered mutants with their source lines, tests that kill no
    mutants, and tests whose kill set is contained in another test's kill set. When a
    baseline report is supplied, lists mutants the baseline killed that are no longer
    killed and exits with code 1 if any exist.

    The kill matrix is only complete when Stryker ran with --disable-bail; otherwise
    each killed mutant records just the first failing test.

.PARAMETER ReportPath
    Path to the current mutation-report.json.

.PARAMETER BaselinePath
    Optional mutation-report.json from an earlier run over the same source and scope.

.PARAMETER Top
    Maximum rows to print per section. Defaults to 50.

.EXAMPLE
    ./Get-MutationSummary.ps1 -ReportPath StrykerOutput/run/reports/mutation-report.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReportPath,

    [string]$BaselinePath,

    [int]$Top = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$KilledStatuses = @('Killed', 'Timeout')
$UntestedStatuses = @('Ignored', 'CompileError', 'Pending')

function Read-Report([string]$path) {
    if (-not (Test-Path $path)) {
        throw "Report not found: $path"
    }

    return Get-Content $path -Raw | ConvertFrom-Json
}

function Get-RelativePath([string]$root, [string]$path) {
    if ($root -and $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($root.Length).TrimStart('\', '/')
    }

    return $path
}

function Get-OptionalProperty($object, [string]$name, $default) {
    if ($object.PSObject.Properties[$name]) {
        return $object.$name
    }

    return $default
}

function Get-Mutants($report) {
    foreach ($file in $report.files.PSObject.Properties) {
        $relative = Get-RelativePath $report.projectRoot $file.Name
        $lines = $file.Value.source -split "`r?`n"
        foreach ($mutant in $file.Value.mutants) {
            $line = $mutant.location.start.line
            $column = $mutant.location.start.column
            [pscustomobject]@{
                Id          = [string]$mutant.id
                File        = $relative
                Line        = $line
                Column      = $column
                Mutator     = $mutant.mutatorName
                Replacement = Get-OptionalProperty $mutant 'replacement' ''
                Status      = $mutant.status
                Reason      = Get-OptionalProperty $mutant 'statusReason' ''
                KilledBy    = @(Get-OptionalProperty $mutant 'killedBy' @())
                SourceLine  = if ($line -ge 1 -and $line -le $lines.Count) { $lines[$line - 1].Trim() } else { '' }
                Key         = '{0}|{1}:{2}|{3}|{4}' -f $relative, $line, $column, $mutant.mutatorName, (Get-OptionalProperty $mutant 'replacement' '')
            }
        }
    }
}

# Maps each test id to its test method. Runners may report one id per theory or one per data row
# (with or without arguments in the name), so redundancy is judged per method, never per row.
function Get-TestMethods($report) {
    $methods = @{}
    foreach ($file in (Get-OptionalProperty $report 'testFiles' ([pscustomobject]@{})).PSObject.Properties) {
        foreach ($test in $file.Value.tests) {
            $methods[[string]$test.id] = $test.name -replace '\s*\(.*\)\s*$', ''
        }
    }

    return $methods
}

function ConvertTo-MethodKills($killedMutants, $testMethods) {
    foreach ($mutant in $killedMutants) {
        [pscustomobject]@{
            Id       = $mutant.Id
            KilledBy = @($mutant.KilledBy | ForEach-Object { if ($testMethods.ContainsKey($_)) { $testMethods[$_] } else { $_ } } | Select-Object -Unique)
        }
    }
}

function New-KillSets($testIds, $killedMutants) {
    $sets = @{}
    foreach ($id in $testIds) {
        $sets[$id] = [System.Collections.Generic.HashSet[string]]::new()
    }

    foreach ($mutant in $killedMutants) {
        foreach ($testId in $mutant.KilledBy) {
            if (-not $sets.ContainsKey($testId)) {
                $sets[$testId] = [System.Collections.Generic.HashSet[string]]::new()
            }

            [void]$sets[$testId].Add($mutant.Id)
        }
    }

    return $sets
}

function Get-SubsumedTests($killSets, $mutantKillers) {
    foreach ($testId in $killSets.Keys) {
        $kills = $killSets[$testId]
        if ($kills.Count -eq 0) {
            continue
        }

        # Any superset of this test's kills must also kill its first mutant.
        $firstMutant = [System.Linq.Enumerable]::First($kills)
        $supersets = @($mutantKillers[$firstMutant] | Where-Object { $_ -ne $testId -and $kills.IsSubsetOf($killSets[$_]) })
        if ($supersets.Count -gt 0) {
            [pscustomobject]@{
                TestId    = $testId
                Kills     = $kills.Count
                Supersets = $supersets
            }
        }
    }
}

function Format-ShortTestName([string]$name) {
    $parts = $name -split '\.'
    if ($parts.Count -ge 2) {
        return '{0}.{1}' -f $parts[-2], $parts[-1]
    }

    return $name
}

function Write-Limited([string[]]$lines) {
    $lines | Select-Object -First $Top | ForEach-Object { Write-Output $_ }
    if ($lines.Count -gt $Top) {
        Write-Output ("... {0} more" -f ($lines.Count - $Top))
    }
}

function Write-Section([string]$title, [string[]]$lines) {
    Write-Output ''
    Write-Output "## $title"
    if ($lines.Count -eq 0) {
        Write-Output 'None.'
        return
    }

    Write-Limited $lines
}

function Format-Gaps($gaps) {
    foreach ($fileGroup in $gaps | Group-Object File) {
        "### $($fileGroup.Name)"
        foreach ($gap in $fileGroup.Group) {
            "- L{0} [{1}] {2} -> {3}" -f $gap.Line, $gap.Status, $gap.Mutator, $gap.Replacement
            "    {0}" -f $gap.SourceLine
        }
    }
}

function Get-LostKills($baselineMutants, $currentMutants) {
    $current = @{}
    foreach ($mutant in $currentMutants) {
        $current[$mutant.Key] = $mutant
    }

    foreach ($mutant in $baselineMutants | Where-Object { $_.Status -in $KilledStatuses }) {
        $now = if ($current.ContainsKey($mutant.Key)) { $current[$mutant.Key].Status } else { 'Missing' }
        if ($now -notin $KilledStatuses) {
            "- {0} L{1} {2} -> {3} (now {4})" -f $mutant.File, $mutant.Line, $mutant.Mutator, $mutant.Replacement, $now
        }
    }
}

$report = Read-Report $ReportPath
$mutants = @(Get-Mutants $report)
$testMethods = Get-TestMethods $report
$tested = @($mutants | Where-Object { $_.Status -notin $UntestedStatuses })
$killed = @($tested | Where-Object { $_.Status -in $KilledStatuses })
$methodKills = @(ConvertTo-MethodKills $killed $testMethods)
$isScoped = @($mutants | Where-Object { $_.Reason -match 'mutate filter|since' }).Count -gt 0
$maxKillers = ($methodKills | ForEach-Object { $_.KilledBy.Count } | Measure-Object -Maximum).Maximum

Write-Output '# Mutation summary'
Write-Output ("Report: {0}" -f (Resolve-Path $ReportPath))
foreach ($group in $mutants | Group-Object Status | Sort-Object Name) {
    Write-Output ("- {0}: {1}" -f $group.Name, $group.Count)
}

if ($tested.Count -gt 0) {
    Write-Output ("- Score (killed / tested): {0:N2}%" -f (100.0 * $killed.Count / $tested.Count))
}

if ($isScoped) {
    Write-Output '- Scope: PARTIAL (mutate filter or --since). Test redundancy below is relative to this scope only.'
}

if ($killed.Count -gt 0 -and $maxKillers -le 1) {
    Write-Output '- Warning: no mutant has more than one killer. If Stryker ran without --disable-bail, test redundancy below is unreliable.'
}

$gaps = @($tested | Where-Object { $_.Status -in 'Survived', 'NoCoverage' } | Sort-Object File, Line, Column)
Write-Section 'Survived and NoCoverage mutants' @(Format-Gaps $gaps)

$methodNames = @($testMethods.Values | Select-Object -Unique)
$killSets = New-KillSets $methodNames $methodKills
$mutantKillers = @{}
foreach ($mutant in $methodKills) {
    $mutantKillers[$mutant.Id] = $mutant.KilledBy
}

$zeroKill = @($methodNames | Where-Object { $killSets[$_].Count -eq 0 } | ForEach-Object { "- $_" } | Sort-Object)
Write-Section 'Test methods that killed no mutants' $zeroKill

$subsumed = @(Get-SubsumedTests $killSets $mutantKillers | Sort-Object Kills | ForEach-Object {
        $covering = ($_.Supersets | Select-Object -First 3 | ForEach-Object { Format-ShortTestName $_ }) -join ', '
        "- {0} ({1} kills) covered by: {2}" -f $_.TestId, $_.Kills, $covering
    })
Write-Section 'Tests whose kills are all covered by another test' $subsumed

if (-not $BaselinePath) {
    return
}

$lost = @(Get-LostKills @(Get-Mutants (Read-Report $BaselinePath)) $mutants)
Write-Section 'Kills lost versus baseline' $lost
if ($lost.Count -gt 0) {
    exit 1
}
