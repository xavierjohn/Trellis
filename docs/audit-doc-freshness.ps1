#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Derives documentation staleness by comparing each reference doc's `last_verified` front-matter
    date against the last commit that touched the source it documents.

.DESCRIPTION
    audit-stale-docs.ps1 catches stale *wording* from a hand-maintained blocklist, so it only ever
    finds mistakes someone already thought to describe. This script asks a question no blocklist
    can: has the source moved on since anyone last verified the doc?

    Each `trellis-api-<name>.md` is mapped to the package that declares the matching
    `<TrellisApiRefName>`, so the comparison is per-area rather than repo-wide. Cross-cutting docs
    that no single package owns are compared against every package's source.

    This is ADVISORY by default, and that is deliberate. Failing the build on every source-touching
    PR would turn `last_verified` into a rubber-stamped field - bumped to silence CI rather than
    because anyone re-read the doc - which destroys the signal the date exists to carry. Use
    -FailOnStale for a periodic sweep where re-verification is the actual task.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch] $FailOnStale,

    # Suppresses docs whose source moved only slightly after verification, which is usually an
    # unrelated commit in the same package rather than an API change.
    [int] $GraceDays = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$docsDir = Join-Path $RepositoryRoot 'docs/docfx_project/api_reference'

# Docs that describe the framework as a whole, so any package's source can invalidate them.
# Shared with docs/lint-api-reference.ps1 (TRLDOC004) - see that file for why one list.
$docManifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'api-reference-docs.psd1')
$crossCuttingDocs = $docManifest.CrossCuttingDocs

function Get-LastVerified {
    param([string] $Path)

    $lines = Get-Content -LiteralPath $Path -TotalCount 30
    foreach ($line in $lines) {
        if ($line -match '^last_verified:\s*(\S+)') {
            $parsed = [datetime]::MinValue
            if ([datetime]::TryParse($Matches[1], [ref] $parsed)) {
                return $parsed
            }
        }
    }

    return $null
}

Push-Location $RepositoryRoot
try {
    # ---- Map TrellisApiRefName -> owning source directory -------------------------------------
    $ownerByRefName = @{}
    foreach ($csproj in Get-ChildItem -Path $RepositoryRoot -Recurse -Filter '*.csproj' -File) {
        $content = Get-Content -LiteralPath $csproj.FullName -Raw
        if ($content -match '<TrellisApiRefName>([^<]+)</TrellisApiRefName>') {
            $ownerByRefName[$Matches[1].Trim()] = (Split-Path $csproj.FullName -Parent)
        }
    }

    if ($ownerByRefName.Count -eq 0) {
        throw "No projects declaring <TrellisApiRefName> found under $RepositoryRoot."
    }

    $allSourceDirs = @($ownerByRefName.Values | ForEach-Object { Resolve-Path -Relative $_ })

    $results = @()

    foreach ($doc in Get-ChildItem -Path $docsDir -Filter '*.md' -File | Sort-Object Name) {
        if ($doc.Name -eq 'completeness-report.md') { continue }

        $lastVerified = Get-LastVerified $doc.FullName
        if (-not $lastVerified) {
            $results += [pscustomobject]@{
                Doc = $doc.Name; Status = 'no-date'; DaysStale = 0
                LastVerified = $null; SourceChanged = $null; Commit = ''; Subject = ''
            }
            continue
        }

        if ($crossCuttingDocs -contains $doc.Name) {
            $sourceDirs = $allSourceDirs
        }
        elseif ($doc.Name -match '^trellis-api-(.+)\.md$' -and $ownerByRefName.ContainsKey($Matches[1])) {
            $sourceDirs = @(Resolve-Path -Relative $ownerByRefName[$Matches[1]])
        }
        else {
            $results += [pscustomobject]@{
                Doc = $doc.Name; Status = 'unmapped'; DaysStale = 0
                LastVerified = $lastVerified; SourceChanged = $null; Commit = ''; Subject = ''
            }
            continue
        }

        $log = git log -1 --format='%cI%x1f%h%x1f%s' -- @sourceDirs
        if (-not $log) { continue }

        $parts = $log -split "`u{1f}"
        $sourceChanged = [datetime]::Parse($parts[0]).ToUniversalTime()
        $daysStale = [int][math]::Floor(($sourceChanged.Date - $lastVerified.Date).TotalDays)

        $results += [pscustomobject]@{
            Doc = $doc.Name
            Status = if ($daysStale -gt $GraceDays) { 'stale' } else { 'current' }
            DaysStale = $daysStale
            LastVerified = $lastVerified
            SourceChanged = $sourceChanged
            Commit = $parts[1]
            Subject = $parts[2]
        }
    }
}
finally {
    Pop-Location
}

$stale = @($results | Where-Object { $_.Status -eq 'stale' } | Sort-Object DaysStale -Descending)
$problems = @($results | Where-Object { $_.Status -in @('no-date', 'unmapped') })

Write-Host ''
Write-Host "Documentation freshness ($($results.Count) docs; source compared per owning package)"
Write-Host ''

if ($stale.Count -eq 0) {
    Write-Host '  All docs were verified at or after their most recent source change.'
}
else {
    Write-Host "  $($stale.Count) doc(s) whose source changed after last_verified:"
    Write-Host ''
    Write-Host ('  {0,-46} {1,-12} {2,-12} {3,6}  {4}' -f 'DOC', 'VERIFIED', 'SRC CHANGED', 'DAYS', 'LATEST COMMIT')
    foreach ($item in $stale) {
        Write-Host ('  {0,-46} {1,-12} {2,-12} {3,6}  {4} {5}' -f `
            $item.Doc,
            $item.LastVerified.ToString('yyyy-MM-dd'),
            $item.SourceChanged.ToString('yyyy-MM-dd'),
            $item.DaysStale,
            $item.Commit,
            ($item.Subject.Substring(0, [math]::Min(48, $item.Subject.Length))))
    }
}

foreach ($item in $problems) {
    $reason = if ($item.Status -eq 'no-date') { 'has no last_verified front-matter date' }
              else { 'could not be mapped to an owning package' }
    Write-Host "  NOTE: $($item.Doc) $reason."
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @("## Documentation freshness", '')
    if ($stale.Count -eq 0) {
        $summary += 'All reference docs were verified at or after their most recent source change.'
    }
    else {
        $summary += "$($stale.Count) doc(s) whose source changed after ``last_verified``:"
        $summary += ''
        $summary += '| Doc | Verified | Source changed | Days | Latest commit |'
        $summary += '| --- | --- | --- | ---: | --- |'
        foreach ($item in $stale) {
            $summary += "| ``$($item.Doc)`` | $($item.LastVerified.ToString('yyyy-MM-dd')) | $($item.SourceChanged.ToString('yyyy-MM-dd')) | $($item.DaysStale) | ``$($item.Commit)`` $($item.Subject) |"
        }
    }
    $summary -join "`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

Write-Host ''
if ($FailOnStale -and $stale.Count -gt 0) {
    Write-Host "Doc freshness check FAILED: $($stale.Count) doc(s) need re-verification." -ForegroundColor Red
    exit 1
}

exit 0
