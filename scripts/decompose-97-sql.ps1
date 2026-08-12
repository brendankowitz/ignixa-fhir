<#
    Decomposes the legacy 97.sql monolith into one .sql file per top-level
    object, under Ignixa.DataLayer.SqlServer.Database. Run once from the repo
    root: pwsh scripts/decompose-97-sql.ps1

    97.sql's object inventory at the time this script was written (verified
    by direct count): 37 tables (1 discarded, see below) + 1 more
    (EventAgentCheckpoint, hidden inside an IF NOT EXISTS guard -- see step
    1b below) = 38 tables, 1 view, 59 "CREATE PROCEDURE" + 6 "CREATE OR ALTER
    PROCEDURE" = 65 stored procedures, 23 TVP types, 1 sequence, 4 partition
    functions, 4 partition schemes = 136 top-level CREATE statements
    (38+1+65+23+1+4+4=136). Two earlier drafts of this script under-counted
    this: one only matched plain "CREATE PROCEDURE" and summed the category
    counts to 125 in this comment; the next missed EventAgentCheckpoint
    entirely because it isn't column-1-anchored like everything else in the
    file. Both were caught on real runs against 97.sql, not by inspection --
    the 136 count above is empirically verified, not derived.
#>
[CmdletBinding()]
param(
    [string]$SourceSql = "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql",
    [string]$OutputRoot = "src/DataLayer/Ignixa.DataLayer.SqlServer.Database"
)

$ErrorActionPreference = "Stop"

$lines = Get-Content -LiteralPath $SourceSql

$folderByKind = @{
    "TABLE"              = "Tables"
    "VIEW"               = "Views"
    "PROCEDURE"          = "StoredProcedures"
    "PROC"               = "StoredProcedures"
    "TYPE"               = "Types"
    "SEQUENCE"           = "Storage"
    "PARTITION FUNCTION" = "Storage"
    "PARTITION SCHEME"   = "Storage"
}

foreach ($folder in ($folderByKind.Values | Sort-Object -Unique)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot $folder) | Out-Null
}
New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot "Scripts") | Out-Null

# 1. Find every top-level object's starting line. Anchored at column 1 --
#    97.sql is consistently formatted (auto-generated), so this reliably
#    distinguishes real top-level CREATE statements from anything indented
#    inside a procedure body. Six procedures in 97.sql are declared with
#    "CREATE OR ALTER PROCEDURE" rather than plain "CREATE PROCEDURE" -- the
#    optional "OR ALTER" must be matched here or those objects silently
#    vanish into the tail of whatever object precedes them (caught via a
#    real build failure on the first run of this script).
$objectPattern = '^CREATE\s+(?:OR ALTER\s+)?(TABLE|VIEW|PROCEDURE|PROC|TYPE|SEQUENCE|PARTITION FUNCTION|PARTITION SCHEME)\s+(.+)$'
$objects = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $objectPattern) {
        $objects += [PSCustomObject]@{
            StartIndex = $i
            Kind       = $Matches[1]
            RestOfLine = $Matches[2]
        }
    }
}

# 1b. One table -- EventAgentCheckpoint -- is defined inside an indented
#     "IF NOT EXISTS (...) BEGIN CREATE TABLE ... END" guard (evidently a
#     later migration-style addition appended by a different generator pass
#     than the rest of the column-1-anchored file). $objectPattern above
#     can't see it -- it was silently swallowed into DateTimeSearchParam's
#     block and broke that table's batch structure, caught via a real build
#     failure. Detect any such guarded table generically and insert it as
#     its own TABLE object, anchored at the guard's own "IF NOT EXISTS" line
#     so the boundary-computation loop
#     below naturally clips the preceding object before it. The guard
#     wrapper itself is unwrapped when this object's content is written,
#     below, for the same reason the file's main idempotency guard is
#     dropped entirely -- SSDT's dacpac-diff deployment engine supplies its
#     own idempotency.
$guardPattern = '^IF NOT EXISTS \(SELECT'
$hiddenTablePattern = '^\s+CREATE TABLE\s+(.+)$'
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -notmatch $guardPattern) { continue }
    $innerLine = $null
    for ($j = $i + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match $hiddenTablePattern) { $innerLine = $Matches[1]; break }
        if ($lines[$j].Trim() -eq "END") { break }
    }
    if ($null -eq $innerLine) { continue }
    $objects += [PSCustomObject]@{
        StartIndex = $i
        Kind       = "TABLE"
        RestOfLine = $innerLine
    }
}
$objects = @($objects | Sort-Object StartIndex)

if ($objects.Count -ne 136) {
    throw "Expected 136 top-level CREATE statements (incl. guarded tables) in $SourceSql, found $($objects.Count). " +
          "Boundary detection is unreliable for a changed source file -- stop and investigate " +
          "before proceeding; do not adjust this count without re-verifying the real inventory."
}

Write-Host "Found $($objects.Count) top-level objects."

# 2. Extract each object's name from the text following the CREATE <kind>
#    keyword. Handles dbo.Name, [dbo].[Name], and bare Name forms.
function Get-ObjectName([string]$restOfLine) {
    $name = $restOfLine -replace '^\[dbo\]\.\[([^\]]+)\].*$', '$1'
    if ($name -eq $restOfLine) {
        $name = $restOfLine -replace '^dbo\.([A-Za-z0-9_]+).*$', '$1'
    }
    if ($name -eq $restOfLine) {
        $name = $restOfLine -replace '^([A-Za-z0-9_]+).*$', '$1'
    }
    return $name.Trim()
}

for ($idx = 0; $idx -lt $objects.Count; $idx++) {
    $obj = $objects[$idx]
    $obj | Add-Member -NotePropertyName Name -NotePropertyValue (Get-ObjectName $obj.RestOfLine)

    $endIndex = if ($idx -lt $objects.Count - 1) { $objects[$idx + 1].StartIndex - 1 } else { $lines.Count - 1 }

    # Special case: PartitionScheme_ResourceChangeData_Timestamp's real
    # content ends at its own closing ";" -- everything between that and the
    # next object is the dynamic partition-splitting loop (imperative setup,
    # not a static schema object), captured separately below into
    # Script.PostDeployment.sql instead of this object's own file.
    if ($obj.Name -eq "PartitionScheme_ResourceChangeData_Timestamp") {
        for ($j = $obj.StartIndex; $j -le $endIndex; $j++) {
            if ($lines[$j].TrimEnd().EndsWith(";")) { $endIndex = $j; break }
        }
    }

    $obj | Add-Member -NotePropertyName EndIndex -NotePropertyValue $endIndex
}

# 2b. Table blocks in 97.sql commonly bundle more than the CREATE TABLE
#     itself -- a trailing ALTER TABLE ... SET (LOCK_ESCALATION = AUTO),
#     ALTER TABLE ... ADD CONSTRAINT, and/or one or more CREATE INDEX
#     statements, each meant to run as its own batch. Some of these are
#     already GO-separated in the source and some are not (97.sql is
#     inconsistent about it), which SSDT's build-time parser rejects with
#     SQL71006 ("Only one statement is allowed per batch") wherever a GO is
#     missing. This function discards whatever GO placement the source had
#     and re-derives it deterministically from statement boundaries, so every
#     table file ends up correctly batch-separated regardless of the source's
#     inconsistency.
#
#     One table (ResourceChangeType) also carries top-level INSERT
#     statements seeding fixed lookup rows -- imperative data, not schema --
#     so those are diverted out of the table file into the post-deployment
#     script alongside the partition-splitting loop (same rationale as that
#     loop: SSDT Build items model declarative schema, not one-time setup).
function Split-TableBlock([string[]]$blockLines) {
    # Built on System.Collections.Generic.List[T] rather than PowerShell's
    # native @()/+= arrays: an earlier draft used the "@() with += and the
    # unary-comma array-of-arrays trick" idiom here, which silently
    # flattened every nested segment array back down to one-line-per-segment
    # when collected out of a `foreach(){...}` expression -- every line in
    # every table ended up GO-separated from every other line. Caught via a
    # real build run (every single-statement table file broke).
    $noGoLines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $blockLines) { if ($l.Trim() -ne "GO") { $noGoLines.Add($l) } }

    $boundaryPattern = '^(ALTER TABLE|CREATE\s+(UNIQUE\s+)?(CLUSTERED\s+|NONCLUSTERED\s+)?INDEX|INSERT)\b'
    $segments = [System.Collections.Generic.List[object]]::new()
    $current = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $noGoLines) {
        if ($line -match $boundaryPattern -and $current.Count -gt 0) {
            $segments.Add($current)
            $current = [System.Collections.Generic.List[string]]::new()
        }
        $current.Add($line)
    }
    if ($current.Count -gt 0) { $segments.Add($current) }

    $trimmedSegments = [System.Collections.Generic.List[object]]::new()
    foreach ($seg in $segments) {
        $start = 0
        $end = $seg.Count - 1
        while ($start -le $end -and $seg[$start].Trim() -eq "") { $start++ }
        while ($end -ge $start -and $seg[$end].Trim() -eq "") { $end-- }
        $trimmed = [System.Collections.Generic.List[string]]::new()
        for ($k = $start; $k -le $end; $k++) { $trimmed.Add($seg[$k]) }
        $trimmedSegments.Add($trimmed)
    }

    $tableSegments = [System.Collections.Generic.List[object]]::new()
    $seedInsertLines = [System.Collections.Generic.List[string]]::new()
    foreach ($seg in $trimmedSegments) {
        if ($seg.Count -gt 0 -and $seg[0] -match '^INSERT\b') {
            if ($seedInsertLines.Count -gt 0) { $seedInsertLines.Add("") }
            foreach ($l in $seg) { $seedInsertLines.Add($l) }
        } else {
            $tableSegments.Add($seg)
        }
    }

    $resultLines = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $tableSegments.Count; $i++) {
        if ($i -gt 0) {
            $resultLines.Add("")
            $resultLines.Add("GO")
            $resultLines.Add("")
        }
        foreach ($l in $tableSegments[$i]) { $resultLines.Add($l) }
    }

    return [PSCustomObject]@{ TableLines = $resultLines.ToArray(); SeedInsertLines = $seedInsertLines.ToArray() }
}

# 3. Write each object's file, trimming trailing blank lines and stray
#    GO/COMMIT batch-separator residue (the bare COMMIT at 97.sql's line 1021
#    closes the discarded idempotency guard and lands at the tail of
#    WatchdogLeases' generically-computed block -- this trim removes it
#    generically rather than special-casing that one table).
$writtenCount = 0
$seedDataByTable = [ordered]@{}
foreach ($obj in $objects) {
    if ($obj.Kind -eq "TABLE" -and $obj.Name -eq "CurrentResource") {
        Write-Host "Discarding throwaway CREATE TABLE dbo.CurrentResource (line $($obj.StartIndex + 1)) -- a scratch/debugging artifact left in 97.sql, not a real schema object."
        continue
    }

    $blockLines = $lines[$obj.StartIndex..$obj.EndIndex]

    # Six procedures use "CREATE OR ALTER PROCEDURE" in 97.sql (needed there
    # only because the monolith could be re-run against a non-empty
    # database). SSDT's build-time model parser rejects that form outright
    # (SQL70001, caught via a real build run) and it is redundant under SSDT
    # anyway: the deployment engine already diffs against the target and
    # decides CREATE vs. ALTER itself. Normalizing to plain CREATE here does
    # not change the deployed object definition.
    if (($obj.Kind -eq "PROCEDURE" -or $obj.Kind -eq "PROC") -and $blockLines[0] -match '^CREATE\s+OR ALTER\s+') {
        $blockLines[0] = $blockLines[0] -replace '^CREATE\s+OR ALTER\s+', 'CREATE '
    }

    if ($obj.Kind -eq "TABLE") {
        if ($blockLines[0] -match $guardPattern) {
            $beginIdx = 0
            for ($k = 0; $k -lt $blockLines.Count; $k++) {
                if ($blockLines[$k].Trim() -eq "BEGIN") { $beginIdx = $k; break }
            }
            $endIdx = $blockLines.Count - 1
            for ($k = $blockLines.Count - 1; $k -ge 0; $k--) {
                if ($blockLines[$k].Trim() -eq "END") { $endIdx = $k; break }
            }
            $inner = $blockLines[($beginIdx + 1)..($endIdx - 1)]
            $blockLines = $inner | ForEach-Object { if ($_ -like "        *") { $_.Substring(8) } else { $_ } }
        }

        $split = Split-TableBlock $blockLines
        $blockLines = $split.TableLines
        if ($split.SeedInsertLines.Count -gt 0) {
            $seedDataByTable[$obj.Name] = $split.SeedInsertLines
        }
    }

    while ($blockLines.Count -gt 0) {
        $last = $blockLines[-1].Trim()
        if ($last -eq "" -or $last -eq "GO" -or $last -eq "COMMIT") {
            $blockLines = $blockLines[0..($blockLines.Count - 2)]
        } else {
            break
        }
    }

    $folder = $folderByKind[$obj.Kind]
    $outPath = Join-Path $OutputRoot (Join-Path $folder "$($obj.Name).sql")
    Set-Content -LiteralPath $outPath -Value $blockLines -Encoding utf8
    $writtenCount++
}

# 4. Capture the dynamic partition-splitting loop into a post-deployment
#    script. SSDT always runs post-deployment scripts after every schema
#    object in the project, so it is guaranteed to run after the scheme and
#    function it references already exist -- no manual ordering needed.
#    Any diverted table seed-data INSERTs (see Split-TableBlock above) are
#    appended after the loop for the same reason -- they need their target
#    table to already exist.
$schemeObj = $objects | Where-Object { $_.Name -eq "PartitionScheme_ResourceChangeData_Timestamp" }
$schemeIdx = [array]::IndexOf($objects, $schemeObj)
$nextObj = $objects[$schemeIdx + 1]
$loopLines = $lines[($schemeObj.EndIndex + 1)..($nextObj.StartIndex - 1)] | Where-Object { $_.Trim() -ne "" }

$postDeployLines = @() + $loopLines
foreach ($tableName in $seedDataByTable.Keys) {
    $postDeployLines += ""
    $postDeployLines += $seedDataByTable[$tableName]
}

$postDeployPath = Join-Path $OutputRoot "Scripts/Script.PostDeployment.sql"
Set-Content -LiteralPath $postDeployPath -Value $postDeployLines -Encoding utf8

Write-Host "Decomposition complete: $writtenCount object files + 1 post-deployment script written."
