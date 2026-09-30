<#
.SYNOPSIS
    Generates the AgentDocs guidance manifest for Trellis.Core from the shipped reference documents.
.DESCRIPTION
    Trellis.Core is the only package that ships guidance: it carries the whole reference set under
    trellis/. Each document declares how agents should use it in its own front matter
    (agent_usage: required | onDemand | supporting, plus agent_description for required and onDemand
    documents); this script copies those declarations into the manifest, so the document and its
    manifest entry cannot drift. docs/lint-api-reference.ps1 (TRLDOC016) enforces the front matter.
    The package identifier/version are intentionally absent: readers obtain them from NuGet assets.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RepositoryRoot,
    [Parameter(Mandatory)][string] $Output
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$docsRoot = Join-Path (Join-Path (Join-Path $RepositoryRoot 'docs') 'docfx_project') 'api_reference'

function Get-FrontMatterValue {
    param([string[]] $Lines, [string] $Key)
    if ($Lines.Count -lt 2 -or $Lines[0].TrimStart([char] 0xFEFF) -notmatch '^---\s*$') { return $null }
    $end = -1
    for ($i = 1; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^---\s*$') { $end = $i; break }
    }
    if ($end -lt 0) { throw 'front matter is never closed' }
    for ($i = 1; $i -lt $end; $i++) {
        if ($Lines[$i] -match '^#') { throw "front matter contains document body (line $($i + 1)); the closing '---' is misplaced" }
        if ($Lines[$i] -match "^$([regex]::Escape($Key)):\s*(?<value>.*)$") {
            $value = $Matches['value'].Trim()
            if ($value -match '^"(?<inner>.*)"$') {
                if ($Matches['inner'].Contains('\')) { throw "$Key uses a backslash escape, which is not supported" }
                return $Matches['inner']
            }
            if ($value.StartsWith("'") -or $value.StartsWith('"')) { throw "$Key must be a double-quoted or plain scalar" }
            return $value
        }
    }
    return $null
}

$sourceDocs = @(Get-ChildItem -LiteralPath $docsRoot -Filter '*.md' -File |
    Where-Object { $_.Name -ne 'completeness-report.md' })

$documents = @($sourceDocs | ForEach-Object {
    $lines = @(Get-Content -LiteralPath $_.FullName)
    $usage = Get-FrontMatterValue $lines 'agent_usage'
    if ($usage -cnotin @('required', 'onDemand', 'supporting')) {
        throw "$($_.Name) must declare agent_usage as required, onDemand or supporting (found '$usage')"
    }
    $description = Get-FrontMatterValue $lines 'agent_description'
    $entry = [ordered]@{
        path = "trellis/$($_.Name)"
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        usage = $usage
    }
    if ($description) { $entry['description'] = $description.Normalize([System.Text.NormalizationForm]::FormC) }
    elseif ($usage -ne 'supporting') { throw "$($_.Name) is $usage but has no agent_description" }
    $entry
})
$rank = @{ required = 0; onDemand = 1; supporting = 2 }
$sorted = [System.Collections.Generic.List[object]]::new($documents)
$sorted.Sort([Comparison[object]] {
    param($a, $b)
    $byUsage = $rank[$a.usage].CompareTo($rank[$b.usage])
    if ($byUsage -ne 0) { $byUsage } else { [string]::CompareOrdinal($a.path, $b.path) }
})
$documents = @($sorted)

$cohort = @(Get-ChildItem -LiteralPath $RepositoryRoot -Directory -Filter 'Trellis.*' |
    ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'src') -Filter '*.csproj' -File -ErrorAction SilentlyContinue } |
    Where-Object {
        [xml]$candidate = Get-Content -LiteralPath $_.FullName -Raw
        $candidate.SelectSingleNode('//IsPackable')?.InnerText -ne 'false' -and
        $candidate.SelectSingleNode('//PackAsTool')?.InnerText -ne 'true'
    } |
    ForEach-Object { $_.BaseName } | Sort-Object -Unique)

$manifest = [ordered]@{
    schemaVersion = 1
    documents = $documents
    publisherMetadata = [ordered]@{
        'org.trellis' = [ordered]@{ lockstepCohort = $cohort }
    }
}
$directory = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $directory -Force | Out-Null
[System.IO.File]::WriteAllText($Output, ($manifest | ConvertTo-Json -Depth 8) + "`n",
    [System.Text.UTF8Encoding]::new($false))
