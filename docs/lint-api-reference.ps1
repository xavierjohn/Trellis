#Requires -Version 7
<#
.SYNOPSIS
  Lints Trellis API reference markdown for doc regressions.

.DESCRIPTION
  Scans docs/docfx_project/api_reference/*.md and emits MSBuild-compatible
  error diagnostics for blocked patterns.

.PARAMETER RepositoryRoot
  Path to the repository root. Defaults to this script's parent directory.
#>

param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$apiReferenceDir = Join-Path $RepositoryRoot 'docs' | Join-Path -ChildPath 'docfx_project' | Join-Path -ChildPath 'api_reference'
$bareCrossDocLinkPattern = '\]\(trellis-api-[a-z-]+\.md\)'
$fillerTableRowPattern = '\| — \| — \| No (public properties|methods|public methods|properties)\.'
$bareCrossDocLinkAllowlistMarker = 'trellis-doc-lint: allow-bare-cross-doc-link'

if (-not (Test-Path -LiteralPath $apiReferenceDir)) {
    Write-Error "API reference directory not found: $apiReferenceDir"
    exit 1
}

$failed = $false
$markdownFiles = Get-ChildItem -LiteralPath $apiReferenceDir -Filter '*.md' -File | Sort-Object FullName

foreach ($file in $markdownFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName)

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNumber = $i + 1

        if ($line -notmatch $bareCrossDocLinkAllowlistMarker) {
            foreach ($match in [regex]::Matches($line, $bareCrossDocLinkPattern)) {
                $column = $match.Index + 1
                Write-Host "$($file.FullName)($lineNumber,$column): error TRLDOC001: Bare cross-doc trellis-api link must include an anchor. Add a #section anchor or append '<!-- $bareCrossDocLinkAllowlistMarker -->' for an intentional exception."
                $failed = $true
            }
        }

        foreach ($match in [regex]::Matches($line, $fillerTableRowPattern)) {
            $column = $match.Index + 1
            Write-Host "$($file.FullName)($lineNumber,$column): error TRLDOC002: Filler table rows like '| — | — | No public properties.' are not allowed in API reference docs. Remove the row or document real public surface."
            $failed = $true
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "API reference lint passed: scanned $($markdownFiles.Count) markdown files."