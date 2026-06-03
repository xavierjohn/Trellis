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
$anchoredLinkPattern = '\]\((?<targetFile>trellis-api-[a-z-]+\.md)?#(?<anchor>[^)\s]+)\)'
$bareCrossDocLinkAllowlistMarker = 'trellis-doc-lint: allow-bare-cross-doc-link'
$brokenAnchorAllowlistMarker = 'trellis-doc-lint: allow-broken-anchor'

function Get-TrellisDocSlug {
    param(
        [Parameter(Mandatory = $true)]
        [string] $HeadingText
    )

    $builder = [System.Text.StringBuilder]::new()
    $normalizedHeading = $HeadingText.Replace('`', '').ToLowerInvariant()

    foreach ($character in $normalizedHeading.ToCharArray()) {
        if ([char]::IsLetterOrDigit($character) -or $character -eq '-' -or $character -eq '_') {
            [void] $builder.Append($character)
            continue
        }

        if ([char]::IsWhiteSpace($character)) {
            [void] $builder.Append('-')
        }
    }

    return $builder.ToString().TrimStart('-')
}

function Get-LevenshteinDistance {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Left,

        [Parameter(Mandatory = $true)]
        [string] $Right,

        [int] $MaximumDistance = 3
    )

    $leftLength = $Left.Length
    $rightLength = $Right.Length

    if ([Math]::Abs($leftLength - $rightLength) -gt $MaximumDistance) {
        return $MaximumDistance + 1
    }

    if ($leftLength -eq 0) {
        return $rightLength
    }

    if ($rightLength -eq 0) {
        return $leftLength
    }

    $previousRow = [int[]]::new($rightLength + 1)
    $currentRow = [int[]]::new($rightLength + 1)

    for ($rightIndex = 0; $rightIndex -le $rightLength; $rightIndex++) {
        $previousRow[$rightIndex] = $rightIndex
    }

    for ($leftIndex = 1; $leftIndex -le $leftLength; $leftIndex++) {
        $currentRow[0] = $leftIndex

        for ($rightIndex = 1; $rightIndex -le $rightLength; $rightIndex++) {
            $cost = if ($Left[$leftIndex - 1] -eq $Right[$rightIndex - 1]) { 0 } else { 1 }
            $deletion = $previousRow[$rightIndex] + 1
            $insertion = $currentRow[$rightIndex - 1] + 1
            $substitution = $previousRow[$rightIndex - 1] + $cost
            $currentRow[$rightIndex] = [Math]::Min([Math]::Min($deletion, $insertion), $substitution)
        }

        $rowSwap = $previousRow
        $previousRow = $currentRow
        $currentRow = $rowSwap
    }

    return $previousRow[$rightLength]
}

function Get-ClosestAnchorSuggestion {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Anchor,

        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]] $Candidates
    )

    $substringMatches = @(
        $Candidates |
            Where-Object { $_.IndexOf($Anchor, [StringComparison]::Ordinal) -ge 0 -or $Anchor.IndexOf($_, [StringComparison]::Ordinal) -ge 0 } |
            Sort-Object @{ Expression = { [Math]::Abs($_.Length - $Anchor.Length) } }, @{ Expression = { $_.Length } }, @{ Expression = { $_ } }
    )

    if ($substringMatches.Count -gt 0) {
        return $substringMatches[0]
    }

    $bestMatch = $null
    $bestDistance = 4

    foreach ($candidate in $Candidates) {
        $distance = Get-LevenshteinDistance -Left $Anchor -Right $candidate -MaximumDistance 3

        if ($distance -le 3 -and $distance -lt $bestDistance) {
            $bestMatch = $candidate
            $bestDistance = $distance
        }
    }

    return $bestMatch
}

function New-HeadingIndex {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo[]] $MarkdownFiles
    )

    $indexByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($file in $MarkdownFiles) {
        $slugCounts = @{}
        $slugs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $orderedSlugs = [System.Collections.Generic.List[string]]::new()
        $lines = @(Get-Content -LiteralPath $file.FullName)

        foreach ($line in $lines) {
            $headingMatch = [regex]::Match($line, '^#{1,6}\s+(.+)$')

            if (-not $headingMatch.Success) {
                continue
            }

            $baseSlug = Get-TrellisDocSlug -HeadingText $headingMatch.Groups[1].Value

            if ($slugCounts.ContainsKey($baseSlug)) {
                $slugCounts[$baseSlug]++
                $slug = "$baseSlug-$($slugCounts[$baseSlug])"
            }
            else {
                $slugCounts[$baseSlug] = 0
                $slug = $baseSlug
            }

            [void] $slugs.Add($slug)
            [void] $orderedSlugs.Add($slug)
        }

        $indexByPath[$file.FullName] = [pscustomobject] @{
            Slugs = $slugs
            OrderedSlugs = $orderedSlugs
        }
    }

    return $indexByPath
}

if (-not (Test-Path -LiteralPath $apiReferenceDir)) {
    Write-Error "API reference directory not found: $apiReferenceDir"
    exit 1
}

$failed = $false
$markdownFiles = Get-ChildItem -LiteralPath $apiReferenceDir -Filter '*.md' -File | Sort-Object FullName
$headingIndexByPath = New-HeadingIndex -MarkdownFiles $markdownFiles

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

        if ($line -match $brokenAnchorAllowlistMarker) {
            continue
        }

        foreach ($match in [regex]::Matches($line, $anchoredLinkPattern)) {
            $anchor = $match.Groups['anchor'].Value
            $targetFile = $match.Groups['targetFile'].Value
            $isSameFileLink = [string]::IsNullOrEmpty($targetFile)
            $targetPath = if ($isSameFileLink) { $file.FullName } else { [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $targetFile)) }
            $targetDisplay = if ($isSameFileLink) { '(self)' } else { $targetFile }
            $targetIndex = $null

            if ($headingIndexByPath.TryGetValue($targetPath, [ref] $targetIndex) -and $targetIndex.Slugs.Contains($anchor)) {
                continue
            }

            $column = $match.Index + 1
            $suggestion = if ($null -eq $targetIndex) { $null } else { Get-ClosestAnchorSuggestion -Anchor $anchor -Candidates $targetIndex.OrderedSlugs }
            $message = "$($file.FullName)($lineNumber,$column): error TRLDOC003: Anchor '#$anchor' does not resolve in '$targetDisplay'."

            if (-not [string]::IsNullOrEmpty($suggestion)) {
                $message += " Did you mean '#$suggestion'?"
            }

            Write-Host $message
            $failed = $true
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "API reference lint passed: scanned $($markdownFiles.Count) markdown files."