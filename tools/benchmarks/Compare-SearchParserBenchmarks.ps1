[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaselineCsv,

    [Parameter(Mandatory)]
    [string] $ReplacementCsv,

    [Parameter(Mandatory)]
    [ValidateSet('Passed', 'Failed')]
    [string] $CorrectnessStatus,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [switch] $AcceptBlockingRegression
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture
$requiredCases = @(
    'Simple',
    'Modified',
    'TypedChain',
    'NestedReverseChain',
    'EscapedAlternative',
    'Composite'
)

function Get-RequiredValue {
    param(
        [Parameter(Mandatory)]
        [psobject] $Row,

        [Parameter(Mandatory)]
        [string] $Column,

        [Parameter(Mandatory)]
        [string] $Case
    )

    $property = $Row.PSObject.Properties[$Column]
    if ($null -eq $property) {
        throw "CSV row for case '$Case' does not contain required column '$Column'."
    }

    $value = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "CSV row for case '$Case' has an empty '$Column' value."
    }

    return $value
}

function Convert-DurationToNanoseconds {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -notmatch '^([0-9]+(?:\.[0-9]+)?)\s*(ns|us|μs|µs|ms|s)$') {
        throw "Unsupported duration '$Value'."
    }

    $number = [double]::Parse($Matches[1], $culture)
    $multiplier = switch ($Matches[2]) {
        'ns' { 1.0; break }
        'us' { 1e3; break }
        'μs' { 1e3; break }
        'µs' { 1e3; break }
        'ms' { 1e6; break }
        's' { 1e9; break }
        default { throw "Unsupported duration unit '$($Matches[2])'." }
    }

    return $number * $multiplier
}

function Convert-Bytes {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    if ($normalized -notmatch '^([0-9]+(?:\.[0-9]+)?)\s*(B|KB|MB|GB)$') {
        throw "Unsupported allocation '$Value'."
    }

    $number = [double]::Parse($Matches[1], $culture)
    $multiplier = switch ($Matches[2]) {
        'B' { 1.0; break }
        'KB' { 1024.0; break }
        'MB' { 1024.0 * 1024.0; break }
        'GB' { 1024.0 * 1024.0 * 1024.0; break }
        default { throw "Unsupported allocation unit '$($Matches[2])'." }
    }

    return $number * $multiplier
}

function Convert-Gen0 {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    return [double]::Parse($normalized, $culture)
}

function Get-PercentChange {
    param(
        [double] $Before,
        [double] $After
    )

    if ($Before -eq 0.0) {
        if ($After -eq 0.0) {
            return 0.0
        }

        return [double]::PositiveInfinity
    }

    return (($After - $Before) / $Before) * 100.0
}

function Format-Number {
    param(
        [double] $Value
    )

    if ([double]::IsPositiveInfinity($Value)) {
        return '∞'
    }

    if ([double]::IsNegativeInfinity($Value)) {
        return '-∞'
    }

    return $Value.ToString('N2', $culture)
}

function Format-Percent {
    param(
        [double] $Value
    )

    if ([double]::IsPositiveInfinity($Value)) {
        return '+∞%'
    }

    if ([double]::IsNegativeInfinity($Value)) {
        return '-∞%'
    }

    return "$($Value.ToString('+0.00;-0.00;0.00', $culture))%"
}

function Get-CaseMap {
    param(
        [Parameter(Mandatory)]
        [object[]] $Rows,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $map = @{}
    foreach ($row in $Rows) {
        $caseName = [string]$row.Case
        if ([string]::IsNullOrWhiteSpace($caseName)) {
            throw "$Label CSV contains a row with an empty Case value."
        }

        if ($map.ContainsKey($caseName)) {
            throw "$Label CSV contains duplicate case '$caseName'."
        }

        $map[$caseName] = $row
    }

    return $map
}

function Assert-ExactCases {
    param(
        [Parameter(Mandatory)]
        [object[]] $Rows,

        [Parameter(Mandatory)]
        [hashtable] $CaseMap,

        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [string[]] $RequiredCases
    )

    $requiredCount = @($RequiredCases).Count
    $rowCount = @($Rows).Count
    if ($rowCount -ne $requiredCount) {
        throw "$Label CSV must contain exactly $requiredCount rows/cases; found $rowCount."
    }

    $actualCases = @($CaseMap.Keys)
    $missingCases = @($RequiredCases | Where-Object { -not $CaseMap.ContainsKey($_) })
    $extraCases = @($actualCases | Where-Object { $_ -notin $RequiredCases })

    if ($missingCases.Count -gt 0 -or $extraCases.Count -gt 0) {
        $missingText = if ($missingCases.Count -gt 0) { $missingCases -join ', ' } else { '(none)' }
        $extraText = if ($extraCases.Count -gt 0) { $extraCases -join ', ' } else { '(none)' }
        throw "$Label CSV case set mismatch. Missing: $missingText. Extra: $extraText."
    }
}

$baselineRows = Import-Csv -LiteralPath $BaselineCsv -Delimiter ','
$replacementRows = Import-Csv -LiteralPath $ReplacementCsv -Delimiter ','
$baselineByCase = Get-CaseMap -Rows $baselineRows -Label 'Baseline'
$replacementByCase = Get-CaseMap -Rows $replacementRows -Label 'Replacement'

Assert-ExactCases -Rows $baselineRows -CaseMap $baselineByCase -Label 'Baseline' -RequiredCases $requiredCases
Assert-ExactCases -Rows $replacementRows -CaseMap $replacementByCase -Label 'Replacement' -RequiredCases $requiredCases

$comparisons = foreach ($caseName in $requiredCases) {
    $baseline = $baselineByCase[$caseName]
    $replacement = $replacementByCase[$caseName]

    $baselineMeanNs = Convert-DurationToNanoseconds (Get-RequiredValue -Row $baseline -Column 'Mean' -Case $caseName)
    $replacementMeanNs = Convert-DurationToNanoseconds (Get-RequiredValue -Row $replacement -Column 'Mean' -Case $caseName)

    if ($baselineMeanNs -le 0.0 -or $replacementMeanNs -le 0.0) {
        throw "Case '$caseName' has a non-positive mean duration."
    }

    $baselineOpsPerSecond = 1e9 / $baselineMeanNs
    $replacementOpsPerSecond = 1e9 / $replacementMeanNs
    $baselineAllocatedBytes = Convert-Bytes (Get-RequiredValue -Row $baseline -Column 'Allocated' -Case $caseName)
    $replacementAllocatedBytes = Convert-Bytes (Get-RequiredValue -Row $replacement -Column 'Allocated' -Case $caseName)
    $baselineGen0 = Convert-Gen0 (Get-RequiredValue -Row $baseline -Column 'Gen0' -Case $caseName)
    $replacementGen0 = Convert-Gen0 (Get-RequiredValue -Row $replacement -Column 'Gen0' -Case $caseName)

    [pscustomobject]@{
        Case = $caseName
        BaselineMeanNs = $baselineMeanNs
        ReplacementMeanNs = $replacementMeanNs
        MeanDeltaPercent = Get-PercentChange -Before $baselineMeanNs -After $replacementMeanNs
        BaselineOpsPerSecond = $baselineOpsPerSecond
        ReplacementOpsPerSecond = $replacementOpsPerSecond
        OpsDeltaPercent = Get-PercentChange -Before $baselineOpsPerSecond -After $replacementOpsPerSecond
        BaselineAllocatedBytes = $baselineAllocatedBytes
        ReplacementAllocatedBytes = $replacementAllocatedBytes
        AllocationDeltaPercent = Get-PercentChange -Before $baselineAllocatedBytes -After $replacementAllocatedBytes
        BaselineGen0 = $baselineGen0
        ReplacementGen0 = $replacementGen0
        Gen0DeltaPercent = Get-PercentChange -Before $baselineGen0 -After $replacementGen0
    }
}

$geometricMeanRatio = [Math]::Exp(
    ($comparisons |
        ForEach-Object { [Math]::Log($_.ReplacementMeanNs / $_.BaselineMeanNs) } |
        Measure-Object -Average).Average)

$geometricMeanChangePercent = ($geometricMeanRatio - 1.0) * 100.0

$blockingRegression = @($comparisons | Where-Object {
        $_.MeanDeltaPercent -gt 10.0 -or
        $_.AllocationDeltaPercent -gt 10.0 -or
        $_.Gen0DeltaPercent -gt 10.0
    }).Count -gt 0

$faster = $geometricMeanChangePercent -le -5.0 -and
    !($comparisons | Where-Object { $_.MeanDeltaPercent -gt 5.0 }) -and
    !($comparisons | Where-Object { $_.AllocationDeltaPercent -gt 0.0 }) -and
    !($comparisons | Where-Object { $_.Gen0DeltaPercent -gt 0.0 })

$classification = if ($faster) {
    'Faster'
} elseif ($geometricMeanChangePercent -ge 5.0) {
    'Slower'
} elseif ([Math]::Abs($geometricMeanChangePercent) -lt 5.0 -and -not $blockingRegression) {
    'Equivalent within 5%'
} else {
    'Mixed'
}

$acceptance = if ($CorrectnessStatus -eq 'Failed') {
    'Rejected: correctness failed.'
} elseif ($blockingRegression -and -not $AcceptBlockingRegression) {
    'Blocked: regression exceeds the 10% blocking threshold. Investigate and obtain explicit user acceptance.'
} elseif ($blockingRegression) {
    'Accepted only because -AcceptBlockingRegression was explicitly provided after investigation.'
} else {
    'Accepted: correctness passed and no blocking regression was detected.'
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Search parser benchmark comparison')
$lines.Add('')
$lines.Add("**Correctness:** **$CorrectnessStatus**")
$lines.Add('')
$lines.Add("**Classification:** **$classification**")
$lines.Add('')
$lines.Add("**Blocking regression:** **$(if ($blockingRegression) { 'Yes' } else { 'No' })**")
$lines.Add('')
$lines.Add("**Acceptance:** $acceptance")
$lines.Add('')
$lines.Add("**Geometric mean time change:** $(Format-Percent -Value $geometricMeanChangePercent)")
$lines.Add('')
$lines.Add('| Case | Baseline mean (ns) | Replacement mean (ns) | Mean Δ | Baseline ops/s | Replacement ops/s | Ops/s Δ | Baseline allocated (B) | Replacement allocated (B) | Allocation Δ | Baseline Gen0 | Replacement Gen0 | Gen0 Δ |')
$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')

foreach ($comparison in $comparisons) {
    $lines.Add(
        "| $($comparison.Case) | $(Format-Number -Value $comparison.BaselineMeanNs) | $(Format-Number -Value $comparison.ReplacementMeanNs) | $(Format-Percent -Value $comparison.MeanDeltaPercent) | $(Format-Number -Value $comparison.BaselineOpsPerSecond) | $(Format-Number -Value $comparison.ReplacementOpsPerSecond) | $(Format-Percent -Value $comparison.OpsDeltaPercent) | $(Format-Number -Value $comparison.BaselineAllocatedBytes) | $(Format-Number -Value $comparison.ReplacementAllocatedBytes) | $(Format-Percent -Value $comparison.AllocationDeltaPercent) | $(Format-Number -Value $comparison.BaselineGen0) | $(Format-Number -Value $comparison.ReplacementGen0) | $(Format-Percent -Value $comparison.Gen0DeltaPercent) |")
}

$lines.Add('')
$lines.Add('Thresholds: Faster requires geometric mean time <= -5%, no per-case mean > +5%, and no allocation or Gen0 increase. Slower is geometric mean >= +5%. Equivalent within 5% requires |geometric mean| < 5% and no blocking regression. Any per-case mean/allocation/Gen0 increase > +10% is a blocking regression.')
$lines.Add('')
$lines.Add('Zero-denominator handling: percent change is 0% for 0->0, +∞% for 0->nonzero, and otherwise ((replacement-baseline)/baseline)*100.')

$outputDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

[IO.File]::WriteAllLines($OutputPath, $lines)
$report = $lines -join [Environment]::NewLine
$report

$mustFail = $CorrectnessStatus -eq 'Failed' -or ($blockingRegression -and -not $AcceptBlockingRegression)
if ($mustFail) {
    exit 1
}
