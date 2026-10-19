<#
.SYNOPSIS
    Lists .NET test projects and the production project Stryker.NET should mutate for each.

.DESCRIPTION
    Finds test projects (projects referencing Microsoft.NET.Test.Sdk or setting IsTestProject)
    and lists their project references, excluding other test projects, as candidates. The
    suggested target is, in order: the reference named like the test
    project without its test suffix, the only reference, or the longest reference whose name is a
    dotted prefix of the test project name. Otherwise Target is empty and Candidates lists all
    non-test references for a human or agent to choose from.

    Output is an object per test project so callers can format or filter it.

.PARAMETER Root
    Repository root to search. Defaults to the current directory.

.EXAMPLE
    ./Get-MutationTargets.ps1 | Format-Table TestProject, Target, Candidates
#>
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TestMarkers = 'Microsoft\.NET\.Test\.Sdk|<IsTestProject>\s*true'
$TestSuffix = '\.(Unit|Integration|E2E|Functional)?Tests?$'
$ExcludedDirectories = '[\\/](bin|obj|node_modules|\.git)[\\/]'

function Test-IsTestProject([System.IO.FileInfo]$project) {
    return [bool](Select-String -Path $project.FullName -Pattern $TestMarkers -Quiet)
}

function Get-ProjectReferences([System.IO.FileInfo]$project) {
    [xml]$xml = Get-Content $project.FullName -Raw
    foreach ($node in $xml.SelectNodes('//ProjectReference')) {
        $path = Join-Path $project.DirectoryName $node.Include
        [System.IO.Path]::GetFileName($path)
    }
}

$projects = Get-ChildItem $Root -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch $ExcludedDirectories }
$testProjectNames = @($projects | Where-Object { Test-IsTestProject $_ } | ForEach-Object { $_.Name })

foreach ($project in $projects | Where-Object { $_.Name -in $testProjectNames } | Sort-Object Name) {
    $references = @(Get-ProjectReferences $project)
    $candidates = @($references | Where-Object { $_ -notin $testProjectNames })
    $testBaseName = $project.BaseName -replace $TestSuffix, ''
    $expected = "$testBaseName.csproj"
    $prefixMatch = $candidates |
        Where-Object { $testBaseName.StartsWith([System.IO.Path]::GetFileNameWithoutExtension($_) + '.', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object Length -Descending |
        Select-Object -First 1
    $target = if ($candidates -contains $expected) { $expected }
        elseif ($candidates.Count -eq 1) { $candidates[0] }
        elseif ($prefixMatch) { $prefixMatch }
        else { '' }

    [pscustomobject]@{
        TestProject = [System.IO.Path]::GetRelativePath($Root, $project.FullName)
        Target      = $target
        Candidates  = $candidates -join ', '
    }
}
