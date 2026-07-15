Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath
$baselineCsv = Join-Path $repositoryRoot 'docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv'
$comparisonScript = Join-Path $repositoryRoot 'tools/benchmarks/Compare-SearchParserBenchmarks.ps1'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "ignixa-search-parser-$([Guid]::NewGuid().ToString('N'))"
$script:reportCounter = 0
$comparisonTolerance = 1e-9
$caseCellIndexes = @{
    'Mean Δ' = 3
    'Allocation Δ' = 9
    'Gen0 Δ' = 12
}

function Get-CaseRow {
    param(
        [Parameter(Mandatory)]
        [object[]] $Rows,

        [Parameter(Mandatory)]
        [string] $CaseName
    )

    $matches = @($Rows | Where-Object Case -eq $CaseName)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$CaseName' row; found $($matches.Count)."
    }

    return $matches[0]
}

function Format-InvariantNumber {
    param(
        [Parameter(Mandatory)]
        [double] $Value
    )

    return $Value.ToString('G17', $culture)
}

function Get-Scalar {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $match = [Text.RegularExpressions.Regex]::Match(
        $Value.Trim(),
        '^([+-]?(?:[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|Infinity|NaN))\s*(\S+)$')
    if (-not $match.Success) {
        throw "Unsupported scalar '$Value'."
    }

    return [pscustomobject]@{
        Number = [double]::Parse($match.Groups[1].Value, $culture)
        Unit = $match.Groups[2].Value
    }
}

function Set-ScaledMetric {
    param(
        [Parameter(Mandatory)]
        [psobject] $Row,

        [Parameter(Mandatory)]
        [string] $PropertyName,

        [Parameter(Mandatory)]
        [double] $Factor
    )

    $value = Get-Scalar -Value ([string]$Row.$PropertyName)
    $Row.$PropertyName = "$(Format-InvariantNumber -Value ($value.Number * $Factor)) $($value.Unit)"
}

function Set-MetricPercentChange {
    param(
        [Parameter(Mandatory)]
        [psobject] $Row,

        [Parameter(Mandatory)]
        [string] $PropertyName,

        [Parameter(Mandatory)]
        [double] $PercentChange
    )

    Set-ScaledMetric -Row $Row -PropertyName $PropertyName -Factor (1.0 + ($PercentChange / 100.0))
}

function Write-FixtureCsv {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [scriptblock] $Mutate
    )

    $rows = @(Import-Csv -LiteralPath $baselineCsv)
    & $Mutate $rows
    $rows | Export-Csv -LiteralPath $Path -NoTypeInformation
}

function New-ReportPath {
    param(
        [Parameter(Mandatory)]
        [string] $ScenarioName
    )

    $script:reportCounter++
    return Join-Path $temporaryDirectory ("report-{0:D2}-{1}.md" -f $script:reportCounter, $ScenarioName)
}

function Invoke-Comparison {
    param(
        [Parameter(Mandatory)]
        [string] $ReplacementCsv,

        [Parameter(Mandatory)]
        [string] $ScenarioName,

        [string] $BaselineOverride = $baselineCsv,

        [string[]] $AdditionalArguments = @()
    )

    $reportPath = New-ReportPath -ScenarioName $ScenarioName
    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Force
    }

    if (Test-Path -LiteralPath $reportPath) {
        throw "Expected no pre-existing report at '$reportPath'."
    }

    $output = & pwsh -NoProfile -File $comparisonScript `
        -BaselineCsv $BaselineOverride `
        -ReplacementCsv $ReplacementCsv `
        -CorrectnessStatus Passed `
        -OutputPath $reportPath `
        @AdditionalArguments 2>&1

    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    $reportExists = Test-Path -LiteralPath $reportPath
    $report = if ($reportExists) {
        Get-Content -LiteralPath $reportPath -Raw
    } else {
        ''
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        ReportExists = $reportExists
        ReportPath = $reportPath
        Report = $report
    }
}

function Assert-ReportContains {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Result.Report.Contains($Expected)) {
        throw "$Message Missing report text: $Expected"
    }
}

function Assert-OutputContains {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Result.Output.Contains($Expected)) {
        throw "$Message Missing process output text: $Expected"
    }
}

function Get-ReportLine {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Prefix
    )

    $lines = @($Result.Report -split '\r?\n' | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) })
    if ($lines.Count -ne 1) {
        throw "Expected exactly one report line starting with '$Prefix'; found $($lines.Count)."
    }

    return $lines[0]
}

function Assert-ExactReportLine {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Prefix,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Message
    )

    $actual = Get-ReportLine -Result $Result -Prefix $Prefix
    if ($actual -ne $Expected) {
        throw "$Message Expected line '$Expected', got '$actual'."
    }
}

function Get-CaseRowCells {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $CaseName
    )

    $pattern = "^\|\s*$([Text.RegularExpressions.Regex]::Escape($CaseName))\s*\|"
    $lines = @($Result.Report -split '\r?\n' | Where-Object { $_ -match $pattern })
    if ($lines.Count -ne 1) {
        throw "Expected exactly one markdown row for case '$CaseName'; found $($lines.Count)."
    }

    $cells = @($lines[0].Trim() -replace '^\|', '' -replace '\|$', '' -split '\|' | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 13) {
        throw "Expected 13 markdown cells for case '$CaseName'; found $($cells.Count)."
    }

    return $cells
}

function Get-CaseDeltaCell {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $CaseName,

        [Parameter(Mandatory)]
        [ValidateSet('Mean Δ', 'Allocation Δ', 'Gen0 Δ')]
        [string] $ColumnName
    )

    return (Get-CaseRowCells -Result $Result -CaseName $CaseName)[$caseCellIndexes[$ColumnName]]
}

function Assert-CaseDeltaCell {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $CaseName,

        [Parameter(Mandatory)]
        [ValidateSet('Mean Δ', 'Allocation Δ', 'Gen0 Δ')]
        [string] $ColumnName,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Message
    )

    $actual = Get-CaseDeltaCell -Result $Result -CaseName $CaseName -ColumnName $ColumnName
    if ($actual -ne $Expected) {
        throw "$Message Expected $ColumnName for case '$CaseName' to be '$Expected', got '$actual'."
    }
}

function Assert-RejectedWithoutReport {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Message,

        [string[]] $OutputEvidence = @()
    )

    if ($Result.ExitCode -ne 1) {
        throw "$Message Expected exit code 1, got $($Result.ExitCode)."
    }

    if ($Result.ReportExists) {
        throw "$Message Rejected invocations must not create a report."
    }

    foreach ($expected in $OutputEvidence) {
        Assert-OutputContains -Result $Result -Expected $expected -Message $Message
    }
}

function Assert-AcceptedResult {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Message,

        [string[]] $Evidence = @()
    )

    if ($Result.ExitCode -ne 0) {
        throw "$Message Expected exit code 0, got $($Result.ExitCode)."
    }

    if (-not $Result.ReportExists) {
        throw "$Message Expected a report at '$($Result.ReportPath)'."
    }

    Assert-ReportContains -Result $Result -Expected '**Blocking regression:** **No**' -Message $Message
    Assert-ReportContains -Result $Result -Expected '**Acceptance:** Accepted: correctness passed and no blocking regression was detected.' -Message $Message

    foreach ($expected in $Evidence) {
        Assert-ReportContains -Result $Result -Expected $expected -Message $Message
    }
}

function Assert-PerformanceBlockResult {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $Message,

        [string[]] $Evidence = @()
    )

    if ($Result.ExitCode -ne 1) {
        throw "$Message Expected exit code 1, got $($Result.ExitCode)."
    }

    if (-not $Result.ReportExists) {
        throw "$Message Expected a blocking report at '$($Result.ReportPath)'."
    }

    Assert-ReportContains -Result $Result -Expected '**Blocking regression:** **Yes**' -Message $Message
    Assert-ReportContains -Result $Result -Expected '**Acceptance:** Blocked: one or more ratified performance limits were exceeded. Investigate and obtain explicit user acceptance.' -Message $Message

    foreach ($expected in $Evidence) {
        Assert-ReportContains -Result $Result -Expected $expected -Message $Message
    }
}

function Assert-ParameterValidationFailure {
    param(
        [Parameter(Mandatory)]
        [psobject] $Result,

        [Parameter(Mandatory)]
        [string] $ParameterName,

        [Parameter(Mandatory)]
        [string] $Message
    )

    Assert-RejectedWithoutReport -Result $Result -Message $Message -OutputEvidence @(
        $ParameterName,
        'must be a finite number greater than or equal to 0.'
    )
}

[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $replacementCsv = Join-Path $temporaryDirectory 'replacement.csv'
    $baselineOverrideCsv = Join-Path $temporaryDirectory 'baseline.csv'

    Write-FixtureCsv -Path $replacementCsv -Mutate { param($rows) }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'identical'
    Assert-AcceptedResult -Result $result -Message 'Identical baseline-vs-baseline comparison must succeed.' -Evidence @(
        '**Geometric mean time change:** 0.00%'
    )
    if ($result.Report.Contains('**Classification:** **Faster**')) {
        throw 'Identical baseline-vs-baseline comparison must not be classified Faster.'
    }

    $invalidLimitCases = @(
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = [double]::NaN.ToString($culture) },
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = [double]::PositiveInfinity.ToString($culture) },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = [double]::NaN.ToString($culture) },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = [double]::PositiveInfinity.ToString($culture) },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = [double]::NaN.ToString($culture) },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = [double]::PositiveInfinity.ToString($culture) },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = [double]::NaN.ToString($culture) },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = [double]::PositiveInfinity.ToString($culture) }
    )

    foreach ($invalidLimitCase in $invalidLimitCases) {
        $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName "invalid-$($invalidLimitCase.Parameter)-$($invalidLimitCase.Value)" -AdditionalArguments @(
            "-$($invalidLimitCase.Parameter)",
            [string]$invalidLimitCase.Value
        )
        Assert-ParameterValidationFailure -Result $result -ParameterName $invalidLimitCase.Parameter -Message "Invalid $($invalidLimitCase.Parameter) value '$($invalidLimitCase.Value)' must be rejected."
    }

    Write-FixtureCsv -Path $baselineOverrideCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '-1 ns'
    }
    $result = Invoke-Comparison -BaselineOverride $baselineOverrideCsv -ReplacementCsv $replacementCsv -ScenarioName 'baseline-mean-negative'
    Assert-RejectedWithoutReport -Result $result -Message 'Negative baseline Mean must be rejected before report generation.' -OutputEvidence @(
        "Case 'Simple' baseline Mean",
        'greater than 0'
    )

    Write-FixtureCsv -Path $baselineOverrideCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '1e-300 ns'
    }
    $result = Invoke-Comparison -BaselineOverride $baselineOverrideCsv -ReplacementCsv $replacementCsv -ScenarioName 'baseline-ops-per-second-overflow'
    Assert-RejectedWithoutReport -Result $result -Message 'A tiny baseline Mean that overflows operations per second must be rejected before report generation.' -OutputEvidence @(
        "Case 'Simple' baseline operations per second",
        'greater than 0'
    )

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '1e309 s'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'replacement-mean-huge'
    Assert-RejectedWithoutReport -Result $result -Message 'A huge replacement Mean must be rejected before report generation.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '1e-300 ns'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'replacement-ops-per-second-overflow'
    Assert-RejectedWithoutReport -Result $result -Message 'A tiny replacement Mean that overflows operations per second must be rejected before report generation.' -OutputEvidence @(
        "Case 'Simple' replacement operations per second",
        'greater than 0'
    )

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '-1 B'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'replacement-allocation-negative'
    Assert-RejectedWithoutReport -Result $result -Message 'Negative replacement Allocated must be rejected before report generation.' -OutputEvidence @(
        "Case 'Simple' replacement Allocated",
        'greater than or equal to 0'
    )

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '1e309 GB'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'replacement-allocation-huge'
    Assert-RejectedWithoutReport -Result $result -Message 'A huge replacement Allocated value must be rejected before report generation.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = '-0.01'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'replacement-gen0-negative'
    Assert-RejectedWithoutReport -Result $result -Message 'Negative replacement Gen0 must be rejected before report generation.' -OutputEvidence @(
        "Case 'Simple' replacement Gen0",
        'greater than or equal to 0'
    )

    foreach ($gen0NonFinite in @([double]::NaN, [double]::PositiveInfinity)) {
        Write-FixtureCsv -Path $replacementCsv -Mutate {
            param($rows)
            (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = $gen0NonFinite.ToString($culture)
        }
        $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName "replacement-gen0-$($gen0NonFinite.ToString($culture))"
        Assert-RejectedWithoutReport -Result $result -Message "Non-finite replacement Gen0 value '$($gen0NonFinite.ToString($culture))' must be rejected before report generation." -OutputEvidence @(
            "Case 'Simple' replacement Gen0",
            'greater than or equal to 0'
        )
    }

    Write-FixtureCsv -Path $replacementCsv -Mutate { param($rows) }
    Write-FixtureCsv -Path $baselineOverrideCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '-'
    }
    $result = Invoke-Comparison -BaselineOverride $baselineOverrideCsv -ReplacementCsv $replacementCsv -ScenarioName 'allocation-zero-denominator-block'
    Assert-PerformanceBlockResult -Result $result -Message 'A 0->nonzero allocation change must remain a blocking +∞% regression.' -Evidence @(
        'each individual case allocated-byte regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Allocation Δ' -Expected '+∞%' -Message 'A 0->nonzero allocation change must render +∞% in the Allocation Δ column.'

    Write-FixtureCsv -Path $baselineOverrideCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = '-'
    }
    $result = Invoke-Comparison -BaselineOverride $baselineOverrideCsv -ReplacementCsv $replacementCsv -ScenarioName 'gen0-zero-denominator-block'
    Assert-PerformanceBlockResult -Result $result -Message 'A 0->nonzero Gen0 change must remain a blocking +∞% regression.' -Evidence @(
        'each individual case Gen0 regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Gen0 Δ' -Expected '+∞%' -Message 'A 0->nonzero Gen0 change must render +∞% in the Gen0 Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '170.64 ns'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-mean-boundary'
    Assert-AcceptedResult -Result $result -Message 'Exact +20% Simple mean regression boundary must be accepted.' -Evidence @(
        'each individual case mean regression <= 20%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Mean Δ' -Expected '+20.00%' -Message 'Exact +20% Simple mean regression must appear in the Mean Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        Set-MetricPercentChange -Row (Get-CaseRow -Rows $rows -CaseName 'Simple') -PropertyName 'Mean' -PercentChange (20.0 + ($comparisonTolerance * 0.5))
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-mean-epsilon-inside'
    Assert-AcceptedResult -Result $result -Message 'A mean regression just inside the tolerance must be accepted.' -Evidence @(
        'each individual case mean regression <= 20%'
    )

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        Set-MetricPercentChange -Row (Get-CaseRow -Rows $rows -CaseName 'Simple') -PropertyName 'Mean' -PercentChange (20.0 + ($comparisonTolerance * 2.0))
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-mean-epsilon-outside'
    Assert-PerformanceBlockResult -Result $result -Message 'A mean regression just outside the tolerance must block.' -Evidence @(
        'each individual case mean regression <= 20%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Mean Δ' -Expected '+20.00%' -Message 'A mean regression just outside the tolerance must still report the Mean Δ value in the Mean Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '170.6472 ns'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-mean-over'
    Assert-PerformanceBlockResult -Result $result -Message 'Simple mean regression above +20% must block.' -Evidence @(
        'each individual case mean regression <= 20%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Mean Δ' -Expected '+20.01%' -Message 'Simple mean regression above +20% must be reported in the Mean Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '680 B'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-allocation-boundary'
    Assert-AcceptedResult -Result $result -Message 'Exact +25% Simple allocation regression boundary must be accepted.' -Evidence @(
        'each individual case allocated-byte regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Allocation Δ' -Expected '+25.00%' -Message 'Exact +25% Simple allocation regression must appear in the Allocation Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '681 B'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-allocation-over'
    Assert-PerformanceBlockResult -Result $result -Message 'Simple allocation regression above +25% must block.' -Evidence @(
        'each individual case allocated-byte regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Allocation Δ' -Expected '+25.18%' -Message 'Simple allocation regression above +25% must be reported in the Allocation Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = '0.039375'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-gen0-boundary'
    Assert-AcceptedResult -Result $result -Message 'Exact +25% Simple Gen0 regression boundary must be accepted.' -Evidence @(
        'each individual case Gen0 regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Gen0 Δ' -Expected '+25.00%' -Message 'Exact +25% Simple Gen0 regression must appear in the Gen0 Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = '0.039382'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'simple-gen0-over'
    Assert-PerformanceBlockResult -Result $result -Message 'Simple Gen0 regression above +25% must block.' -Evidence @(
        'each individual case Gen0 regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Gen0 Δ' -Expected '+25.02%' -Message 'Simple Gen0 regression above +25% must be reported in the Gen0 Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        foreach ($row in $rows) {
            Set-ScaledMetric -Row $row -PropertyName 'Mean' -Factor 1.1
        }
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gm-boundary'
    Assert-AcceptedResult -Result $result -Message 'Exact +10% geometric-mean regression boundary must be accepted.' -Evidence @(
        'geometric-mean mean-time regression <= 10%'
    )
    Assert-ExactReportLine -Result $result -Prefix '**Geometric mean time change:** ' -Expected '**Geometric mean time change:** +10.00%' -Message 'Exact +10% geometric-mean regression must appear on the geometric-mean line.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        foreach ($row in $rows) {
            Set-ScaledMetric -Row $row -PropertyName 'Mean' -Factor 1.1001
        }
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gm-over'
    Assert-PerformanceBlockResult -Result $result -Message 'Geometric-mean regression above +10% must block.' -Evidence @(
        'geometric-mean mean-time regression <= 10%'
    )
    Assert-ExactReportLine -Result $result -Prefix '**Geometric mean time change:** ' -Expected '**Geometric mean time change:** +10.01%' -Message 'Geometric-mean regression above +10% must appear on the geometric-mean line.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        foreach ($row in $rows) {
            Set-ScaledMetric -Row $row -PropertyName 'Mean' -Factor 1.09
        }
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gm-custom-default-accepted'
    Assert-AcceptedResult -Result $result -Message 'A +9% geometric-mean regression must remain accepted under the default 10% limit.' -Evidence @(
        'geometric-mean mean-time regression <= 10%'
    )
    Assert-ExactReportLine -Result $result -Prefix '**Geometric mean time change:** ' -Expected '**Geometric mean time change:** +9.00%' -Message 'A +9% geometric-mean regression must appear on the geometric-mean line.'

    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gm-custom-block' -AdditionalArguments @(
        '-MaximumGeometricMeanRegressionPercent',
        '8.5'
    )
    Assert-PerformanceBlockResult -Result $result -Message 'A stricter custom geometric-mean limit must be honored.' -Evidence @(
        'geometric-mean mean-time regression <= 8.5%'
    )
    Assert-ExactReportLine -Result $result -Prefix '**Geometric mean time change:** ' -Expected '**Geometric mean time change:** +9.00%' -Message 'A stricter custom geometric-mean limit must preserve the geometric-mean line.'

    Write-FixtureCsv -Path $baselineOverrideCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '1E30 ns'
        (Get-CaseRow -Rows $rows -CaseName 'Modified').Mean = '1E-299 ns'
        (Get-CaseRow -Rows $rows -CaseName 'TypedChain').Mean = '1E-299 ns'
    }
    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '1E-299 ns'
        (Get-CaseRow -Rows $rows -CaseName 'Modified').Mean = '1E1 ns'
        (Get-CaseRow -Rows $rows -CaseName 'TypedChain').Mean = '1E-270 ns'
    }
    $result = Invoke-Comparison -BaselineOverride $baselineOverrideCsv -ReplacementCsv $replacementCsv -ScenarioName 'gm-log-difference-stability' -AdditionalArguments @(
        '-MaximumMeanRegressionPercent',
        '1E303'
    )
    Assert-AcceptedResult -Result $result -Message 'Opposing extreme but finite mean ratios must still produce a stable accepted geometric mean result.' -Evidence @(
        'each individual case mean regression <= 1E+303%'
    )
    Assert-ExactReportLine -Result $result -Prefix '**Geometric mean time change:** ' -Expected '**Geometric mean time change:** 0.00%' -Message 'Opposing extreme but finite mean ratios must yield a 0.00% geometric-mean line.'
    if ($result.Report.Contains('**Classification:** **Faster**')) {
        throw 'Opposing extreme but finite mean ratios must not be classified Faster.'
    }
    Assert-ReportContains -Result $result -Expected '**Classification:** **Equivalent within 5%**' -Message 'Opposing extreme but finite mean ratios must remain equivalent within 5%.'

    Write-FixtureCsv -Path $replacementCsv -Mutate { param($rows) }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'tiny-gm-limit-visible' -AdditionalArguments @(
        '-MaximumGeometricMeanRegressionPercent',
        '1e-16'
    )
    Assert-AcceptedResult -Result $result -Message 'A tiny configured geometric-mean limit must remain visible and nonzero in the report.'
    Assert-ExactReportLine -Result $result -Prefix 'Acceptance limits: ' -Expected 'Acceptance limits: geometric-mean mean-time regression <= 9.9999999999999998E-17%; each individual case mean regression <= 20%; each individual case allocated-byte regression <= 25%; each individual case Gen0 regression <= 25%.' -Message 'A tiny configured geometric-mean limit must use nonzero G17 formatting.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Mean = '167.796 ns'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'mean-custom-default-accepted'
    Assert-AcceptedResult -Result $result -Message 'An +18% Simple mean regression must remain accepted under the default 20% limit.' -Evidence @(
        'each individual case mean regression <= 20%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Mean Δ' -Expected '+18.00%' -Message 'An +18% Simple mean regression must appear in the Mean Δ column.'

    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'mean-custom-block' -AdditionalArguments @(
        '-MaximumMeanRegressionPercent',
        '17.5'
    )
    Assert-PerformanceBlockResult -Result $result -Message 'A stricter custom per-case mean limit must be honored.' -Evidence @(
        'each individual case mean regression <= 17.5%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Mean Δ' -Expected '+18.00%' -Message 'A stricter custom per-case mean limit must preserve the Mean Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Allocated = '650 B'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'allocation-custom-default-accepted'
    Assert-AcceptedResult -Result $result -Message 'A +19.49% Simple allocation regression must remain accepted under the default 25% limit.' -Evidence @(
        'each individual case allocated-byte regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Allocation Δ' -Expected '+19.49%' -Message 'A +19.49% Simple allocation regression must appear in the Allocation Δ column.'

    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'allocation-custom-block' -AdditionalArguments @(
        '-MaximumAllocationRegressionPercent',
        '19'
    )
    Assert-PerformanceBlockResult -Result $result -Message 'A stricter custom allocation limit must be honored.' -Evidence @(
        'each individual case allocated-byte regression <= 19%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Allocation Δ' -Expected '+19.49%' -Message 'A stricter custom allocation limit must preserve the Allocation Δ column.'

    Write-FixtureCsv -Path $replacementCsv -Mutate {
        param($rows)
        (Get-CaseRow -Rows $rows -CaseName 'Simple').Gen0 = '0.0378'
    }
    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gen0-custom-default-accepted'
    Assert-AcceptedResult -Result $result -Message 'A +20% Simple Gen0 regression must remain accepted under the default 25% limit.' -Evidence @(
        'each individual case Gen0 regression <= 25%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Gen0 Δ' -Expected '+20.00%' -Message 'A +20% Simple Gen0 regression must appear in the Gen0 Δ column.'

    $result = Invoke-Comparison -ReplacementCsv $replacementCsv -ScenarioName 'gen0-custom-block' -AdditionalArguments @(
        '-MaximumGen0RegressionPercent',
        '19.5'
    )
    Assert-PerformanceBlockResult -Result $result -Message 'A stricter custom Gen0 limit must be honored.' -Evidence @(
        'each individual case Gen0 regression <= 19.5%'
    )
    Assert-CaseDeltaCell -Result $result -CaseName 'Simple' -ColumnName 'Gen0 Δ' -Expected '+20.00%' -Message 'A stricter custom Gen0 limit must preserve the Gen0 Δ column.'
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
