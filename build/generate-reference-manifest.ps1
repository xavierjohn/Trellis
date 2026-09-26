<#
.SYNOPSIS
    Generates the experimental package guidance manifest from the shipped source documents.
.DESCRIPTION
    Mode is the pair TrellisShipsApiReferenceSet;TrellisShipsOwnApiReference.
    The package identifier/version are intentionally absent: readers obtain them from NuGet assets.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RepositoryRoot,
    [Parameter(Mandatory)][string] $Project,
    [Parameter(Mandatory)][string] $Output,
    [Parameter(Mandatory)][string] $Mode
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = (Resolve-Path (Join-Path $RepositoryRoot '..')).Path
$parts = $Mode.Split(';')
$docsRoot = Join-Path $RepositoryRoot 'docs\docfx_project\api_reference'
[xml] $projectXml = Get-Content -LiteralPath $Project -Raw
$ownedName = [string]$projectXml.SelectSingleNode('//TrellisApiRefName')?.InnerText
$sourceDocs = @()
if ($parts[0] -eq 'true') {
    $sourceDocs = @(Get-ChildItem -LiteralPath $docsRoot -Filter '*.md' -File |
        Where-Object { $_.Name -ne 'completeness-report.md' })
}
elseif ($parts.Count -gt 1 -and $parts[1] -eq 'true') {
    if (-not $ownedName) { throw "Missing TrellisApiRefName for $Project" }
    $sourceDocs = @((Get-Item -LiteralPath (Join-Path $docsRoot "trellis-api-$ownedName.md")))
}

$documents = @($sourceDocs | Sort-Object Name | ForEach-Object {
    [ordered]@{
        path = "trellis/$($_.Name)"
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$entryPoints = if ($documents.Count -eq 0) { @() }
elseif ($parts[0] -eq 'true') { @('trellis/trellis-start-here.md') }
else { @($documents[0].path) }

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
    entryPoints = @($entryPoints)
    publisherMetadata = [ordered]@{
        'org.trellis' = if ($parts[0] -eq 'true') {
            [ordered]@{ lockstepCohort = $cohort }
        } else {
            [ordered]@{ lockstep = $true }
        }
    }
}
$directory = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $directory -Force | Out-Null
[System.IO.File]::WriteAllText($Output, ($manifest | ConvertTo-Json -Depth 8) + "`n",
    [System.Text.UTF8Encoding]::new($false))
