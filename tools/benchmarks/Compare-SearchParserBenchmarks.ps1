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

    [ValidateScript({
        if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
            throw 'MaximumGeometricMeanRegressionPercent must be a finite number greater than or equal to 0.'
        }

        $true
    })]
    [double] $MaximumGeometricMeanRegressionPercent = 10.0,

    [ValidateScript({
        if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
            throw 'MaximumMeanRegressionPercent must be a finite number greater than or equal to 0.'
        }

        $true
    })]
    [double] $MaximumMeanRegressionPercent = 20.0,

    [ValidateScript({
        if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
            throw 'MaximumAllocationRegressionPercent must be a finite number greater than or equal to 0.'
        }

        $true
    })]
    [double] $MaximumAllocationRegressionPercent = 25.0,

    [ValidateScript({
        if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
            throw 'MaximumGen0RegressionPercent must be a finite number greater than or equal to 0.'
        }

        $true
    })]
    [double] $MaximumGen0RegressionPercent = 25.0,

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

function Parse-InvariantDoubleLiteral {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Case,

        [Parameter(Mandatory)]
        [string] $SourceLabel,

        [Parameter(Mandatory)]
        [string] $Metric
    )

    try {
        return [double]::Parse($Value, $culture)
    }
    catch {
        throw "Case '$Case' $SourceLabel $Metric value '$Value' is not a valid numeric literal."
    }
}

function Assert-ValidMetricValue {
    param(
        [double] $Value,
        [Parameter(Mandatory)]
        [string] $Case,
        [Parameter(Mandatory)]
        [string] $SourceLabel,
        [Parameter(Mandatory)]
        [string] $Metric,
        [double] $Minimum = 0.0,
        [switch] $ExclusiveMinimum
    )

    $requirementText = if ($ExclusiveMinimum) {
        "finite and greater than $($Minimum.ToString('G17', $culture))"
    } else {
        "finite and greater than or equal to $($Minimum.ToString('G17', $culture))"
    }

    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        throw "Case '$Case' $SourceLabel $Metric must be $requirementText."
    }

    if ($ExclusiveMinimum) {
        if ($Value -le $Minimum) {
            throw "Case '$Case' $SourceLabel $Metric must be $requirementText."
        }
    } elseif ($Value -lt $Minimum) {
        throw "Case '$Case' $SourceLabel $Metric must be $requirementText."
    }

    return $Value
}

function Convert-DurationToNanoseconds {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Case,

        [Parameter(Mandatory)]
        [string] $SourceLabel
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -notmatch '^([+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(ns|us|μs|µs|ms|s)$') {
        throw "Case '$Case' $SourceLabel Mean value '$Value' is not a supported duration."
    }

    $number = Parse-InvariantDoubleLiteral -Value $Matches[1] -Case $Case -SourceLabel $SourceLabel -Metric 'Mean'
    $multiplier = switch ($Matches[2]) {
        'ns' { 1.0; break }
        'us' { 1e3; break }
        'μs' { 1e3; break }
        'µs' { 1e3; break }
        'ms' { 1e6; break }
        's' { 1e9; break }
        default { throw "Unsupported duration unit '$($Matches[2])'." }
    }

    return Assert-ValidMetricValue -Value ($number * $multiplier) -Case $Case -SourceLabel $SourceLabel -Metric 'Mean' -Minimum 0.0 -ExclusiveMinimum
}

function Convert-Bytes {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Case,

        [Parameter(Mandatory)]
        [string] $SourceLabel
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    if ($normalized -notmatch '^([+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(B|KB|MB|GB)$') {
        throw "Case '$Case' $SourceLabel Allocated value '$Value' is not a supported allocation."
    }

    $number = Parse-InvariantDoubleLiteral -Value $Matches[1] -Case $Case -SourceLabel $SourceLabel -Metric 'Allocated'
    $multiplier = switch ($Matches[2]) {
        'B' { 1.0; break }
        'KB' { 1024.0; break }
        'MB' { 1024.0 * 1024.0; break }
        'GB' { 1024.0 * 1024.0 * 1024.0; break }
        default { throw "Unsupported allocation unit '$($Matches[2])'." }
    }

    return Assert-ValidMetricValue -Value ($number * $multiplier) -Case $Case -SourceLabel $SourceLabel -Metric 'Allocated'
}

function Convert-Gen0 {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Case,

        [Parameter(Mandatory)]
        [string] $SourceLabel
    )

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    $parsed = Parse-InvariantDoubleLiteral -Value $normalized -Case $Case -SourceLabel $SourceLabel -Metric 'Gen0'
    return Assert-ValidMetricValue -Value $parsed -Case $Case -SourceLabel $SourceLabel -Metric 'Gen0'
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

function Format-LimitPercentValue {
    param(
        [double] $Value
    )

    return $Value.ToString('G17', $culture)
}

function Test-PercentExceedsLimit {
    param(
        [double] $Value,
        [double] $Limit,
        [string] $Label = 'Comparison metric'
    )

    if ([double]::IsNaN($Value)) {
        throw "$Label is NaN."
    }

    return ($Value - $Limit) -gt 1e-9
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

    $baselineMeanNs = Convert-DurationToNanoseconds -Value (Get-RequiredValue -Row $baseline -Column 'Mean' -Case $caseName) -Case $caseName -SourceLabel 'baseline'
    $replacementMeanNs = Convert-DurationToNanoseconds -Value (Get-RequiredValue -Row $replacement -Column 'Mean' -Case $caseName) -Case $caseName -SourceLabel 'replacement'

    $baselineOpsPerSecond = Assert-ValidMetricValue -Value (1e9 / $baselineMeanNs) -Case $caseName -SourceLabel 'baseline' -Metric 'operations per second' -Minimum 0.0 -ExclusiveMinimum
    $replacementOpsPerSecond = Assert-ValidMetricValue -Value (1e9 / $replacementMeanNs) -Case $caseName -SourceLabel 'replacement' -Metric 'operations per second' -Minimum 0.0 -ExclusiveMinimum
    $baselineAllocatedBytes = Convert-Bytes -Value (Get-RequiredValue -Row $baseline -Column 'Allocated' -Case $caseName) -Case $caseName -SourceLabel 'baseline'
    $replacementAllocatedBytes = Convert-Bytes -Value (Get-RequiredValue -Row $replacement -Column 'Allocated' -Case $caseName) -Case $caseName -SourceLabel 'replacement'
    $baselineGen0 = Convert-Gen0 -Value (Get-RequiredValue -Row $baseline -Column 'Gen0' -Case $caseName) -Case $caseName -SourceLabel 'baseline'
    $replacementGen0 = Convert-Gen0 -Value (Get-RequiredValue -Row $replacement -Column 'Gen0' -Case $caseName) -Case $caseName -SourceLabel 'replacement'

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
        ForEach-Object { [Math]::Log($_.ReplacementMeanNs) - [Math]::Log($_.BaselineMeanNs) } |
        Measure-Object -Average).Average)

$geometricMeanChangePercent = ($geometricMeanRatio - 1.0) * 100.0

$blockingRegression =
    (Test-PercentExceedsLimit -Value $geometricMeanChangePercent -Limit $MaximumGeometricMeanRegressionPercent -Label 'Geometric mean time change') -or
    @($comparisons | Where-Object {
            (Test-PercentExceedsLimit -Value $_.MeanDeltaPercent -Limit $MaximumMeanRegressionPercent -Label "Case '$($_.Case)' Mean Δ") -or
            (Test-PercentExceedsLimit -Value $_.AllocationDeltaPercent -Limit $MaximumAllocationRegressionPercent -Label "Case '$($_.Case)' Allocation Δ") -or
            (Test-PercentExceedsLimit -Value $_.Gen0DeltaPercent -Limit $MaximumGen0RegressionPercent -Label "Case '$($_.Case)' Gen0 Δ")
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
    'Blocked: one or more ratified performance limits were exceeded. Investigate and obtain explicit user acceptance.'
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
$lines.Add("Acceptance limits: geometric-mean mean-time regression <= $(Format-LimitPercentValue -Value $MaximumGeometricMeanRegressionPercent)%; each individual case mean regression <= $(Format-LimitPercentValue -Value $MaximumMeanRegressionPercent)%; each individual case allocated-byte regression <= $(Format-LimitPercentValue -Value $MaximumAllocationRegressionPercent)%; each individual case Gen0 regression <= $(Format-LimitPercentValue -Value $MaximumGen0RegressionPercent)%.")
$lines.Add('Faster remains stricter: geometric mean time <= -5%, no per-case mean > +5%, and no allocation or Gen0 increase.')
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
