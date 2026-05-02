#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Scores a Q&A bench result file against the question bank's verifier rules.

.DESCRIPTION
  Reads a result JSON written by run-qa-bench.ps1, applies each question's
  verifier (all_of / any_of / none_of), prints a per-question and per-category
  table, and writes a sibling .score.json + .score.md report.

  Returns exit 0 always (this is a measurement, not a gate).

.PARAMETER ResultPath
  Path to a result JSON file (relative or absolute).

.PARAMETER QuestionsPath
  Path to questions.json. Defaults to ./questions.json next to this script.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultPath,
    [string] $QuestionsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchRoot = Split-Path -Parent $PSCommandPath
if (-not $QuestionsPath) { $QuestionsPath = Join-Path $benchRoot 'questions.json' }

$result    = Get-Content -Raw -Path $ResultPath    | ConvertFrom-Json
$qdoc      = Get-Content -Raw -Path $QuestionsPath | ConvertFrom-Json
$questions = @($qdoc.questions)

# index answers by id (case-sensitive match expected from the model)
$answersById = @{}
if ($result.answers) {
    foreach ($a in $result.answers) {
        if ($a -and $a.id) { $answersById[$a.id] = $a.answer }
    }
}

function Test-Verifier {
    param([string] $Answer, $Verifier)
    $a = $Answer
    if (-not $a) { return @{ pass = $false; missing_all = @(); missing_any = @(); hit_none = @(); reason = 'no answer' } }
    $aLower = $a.ToLowerInvariant()

    $missingAll = @()
    if ($Verifier -and $Verifier.PSObject.Properties.Name -contains 'all_of' -and $Verifier.all_of) {
        foreach ($k in @($Verifier.all_of)) {
            if (-not $aLower.Contains($k.ToLowerInvariant())) { $missingAll += $k }
        }
    }
    $missingAny = @()
    if ($Verifier -and $Verifier.PSObject.Properties.Name -contains 'any_of' -and $Verifier.any_of) {
        foreach ($group in @($Verifier.any_of)) {
            $hit = $false
            foreach ($k in @($group)) {
                if ($aLower.Contains($k.ToLowerInvariant())) { $hit = $true; break }
            }
            if (-not $hit) { $missingAny += ,@($group) }
        }
    }
    $hitNone = @()
    if ($Verifier -and $Verifier.PSObject.Properties.Name -contains 'none_of' -and $Verifier.none_of) {
        foreach ($k in @($Verifier.none_of)) {
            if ($aLower.Contains($k.ToLowerInvariant())) { $hitNone += $k }
        }
    }

    $pass = ($missingAll.Count -eq 0) -and ($missingAny.Count -eq 0) -and ($hitNone.Count -eq 0)
    return @{ pass = $pass; missing_all = $missingAll; missing_any = $missingAny; hit_none = $hitNone; reason = '' }
}

$scored = @()
foreach ($q in $questions) {
    $ans = if ($answersById.ContainsKey($q.id)) { $answersById[$q.id] } else { $null }
    $r   = Test-Verifier -Answer $ans -Verifier $q.verifier
    $scored += [pscustomobject]@{
        id           = $q.id
        category     = $q.category
        difficulty   = $q.difficulty
        target_doc   = $q.target_doc
        question     = $q.question
        answer       = $ans
        pass         = $r.pass
        missing_all  = $r.missing_all
        missing_any  = $r.missing_any
        hit_none     = $r.hit_none
    }
}

# --- aggregate ---------------------------------------------------------------
$total       = $scored.Count
$passCount   = @($scored | Where-Object { $_.pass }).Count
$overallPct  = if ($total -gt 0) { [math]::Round(100.0 * $passCount / $total, 1) } else { 0 }

$catLines = @()
$byCat = $scored | Group-Object category | Sort-Object Name
foreach ($g in $byCat) {
    $cTotal = $g.Count
    $cPass  = @($g.Group | Where-Object { $_.pass }).Count
    $cPct   = if ($cTotal -gt 0) { [math]::Round(100.0 * $cPass / $cTotal, 1) } else { 0 }
    $catLines += [pscustomobject]@{ category = $g.Name; pass = $cPass; total = $cTotal; pct = $cPct }
}

# --- console report ---------------------------------------------------------
Write-Host ""
Write-Host ("=== Score: {0} (model={1}, tag={2}) ===" -f (Split-Path -Leaf $ResultPath), $result.meta.model_returned, $result.meta.tag)
Write-Host ("Overall: {0}/{1} ({2}%)`n" -f $passCount, $total, $overallPct)
Write-Host "By category:"
$catLines | Format-Table -AutoSize | Out-Host

Write-Host "Per-question:"
$scored | ForEach-Object {
    $mark = if ($_.pass) { 'PASS' } else { 'FAIL' }
    $color = if ($_.pass) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} ({2}/{3})" -f $mark, $_.id, $_.category, $_.difficulty) -ForegroundColor $color
    if (-not $_.pass) {
        if ($_.missing_all.Count -gt 0) { Write-Host ("       missing all_of: {0}" -f ($_.missing_all -join ', ')) -ForegroundColor DarkYellow }
        if ($_.missing_any.Count -gt 0) {
            $groups = ($_.missing_any | ForEach-Object { '[' + ($_ -join '|') + ']' }) -join ', '
            Write-Host ("       missing any_of: $groups") -ForegroundColor DarkYellow
        }
        if ($_.hit_none.Count -gt 0)    { Write-Host ("       contains none_of: {0}" -f ($_.hit_none -join ', ')) -ForegroundColor DarkYellow }
        $preview = if ($_.answer) { $_.answer } else { '(no answer)' }
        if ($preview.Length -gt 200) { $preview = $preview.Substring(0,200) + '...' }
        Write-Host ("       answer: $preview") -ForegroundColor DarkGray
    }
}

# --- write report files -----------------------------------------------------
$baseOut    = $ResultPath -replace '\.json$',''
$scoreJson  = "$baseOut.score.json"
$scoreMd    = "$baseOut.score.md"

[ordered]@{
    source            = (Resolve-Path $ResultPath).Path
    questions         = (Resolve-Path $QuestionsPath).Path
    model_returned    = $result.meta.model_returned
    tag               = $result.meta.tag
    timestamp_utc     = $result.meta.timestamp_utc
    overall_pass      = $passCount
    overall_total     = $total
    overall_pct       = $overallPct
    by_category       = $catLines
    questions_results = $scored
} | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 -FilePath $scoreJson

$md = @()
$md += "# Q&A bench score: $(Split-Path -Leaf $ResultPath)"
$md += ""
$md += "- Model: ``$($result.meta.model_returned)``"
$md += "- Tag: ``$($result.meta.tag)``"
$md += "- Timestamp (UTC): $($result.meta.timestamp_utc)"
$md += "- Git commit: $($result.meta.git_commit)"
$md += "- **Overall: $passCount / $total ($overallPct%)**"
$md += ""
$md += "## By category"
$md += ""
$md += "| Category | Pass | Total | % |"
$md += "|---|---|---|---|"
foreach ($c in $catLines) { $md += ("| {0} | {1} | {2} | {3}% |" -f $c.category, $c.pass, $c.total, $c.pct) }
$md += ""
$md += "## Per-question"
$md += ""
$md += "| Result | Id | Category | Difficulty | Notes |"
$md += "|---|---|---|---|---|"
foreach ($r in $scored) {
    $mark = if ($r.pass) { '✅' } else { '❌' }
    $notes = @()
    if (-not $r.pass) {
        if ($r.missing_all.Count -gt 0) { $notes += "missing: $($r.missing_all -join ', ')" }
        if ($r.missing_any.Count -gt 0) {
            $groups = @($r.missing_any | ForEach-Object { '[' + (($_ -join ' OR ')) + ']' }) -join ', '
            $notes += "any-of unmet: $groups"
        }
        if ($r.hit_none.Count -gt 0)    { $notes += "anti-pattern: $($r.hit_none -join ', ')" }
    }
    $cell = ($notes -join '; ') -replace '\|','\|'
    $md += ("| {0} | ``{1}`` | {2} | {3} | {4} |" -f $mark, $r.id, $r.category, $r.difficulty, $cell)
}
$md -join "`n" | Out-File -Encoding utf8 -FilePath $scoreMd

Write-Host ""
Write-Host "Wrote:"
Write-Host "  $scoreJson"
Write-Host "  $scoreMd"
