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
$anchoredLinkPattern = '\]\((?:(?<targetFile>[^#?)\s]+\.md)(?:\?[^#)\s]*)?)?#(?<anchor>[^)\s]+)\)'
# CommonMark fences allow at most three leading literal spaces; four spaces start indented code.
$fencedCodeBlockOpeningPattern = '^( {0,3})(?<fence>`{3,}|~{3,})(?<info>.*)$'
$fencedCodeBlockClosingPattern = '^( {0,3})(?<fence>`{3,}|~{3,}) *$'
$bareCrossDocLinkAllowlistMarker = 'trellis-doc-lint: allow-bare-cross-doc-link'
$brokenAnchorAllowlistMarker = 'trellis-doc-lint: allow-broken-anchor'

function Get-FencedCodeBlockOpeningMarker {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Line
    )

    $fenceMatch = [regex]::Match($Line, $fencedCodeBlockOpeningPattern)

    if (-not $fenceMatch.Success) {
        return $null
    }

    $currentFenceMarker = $fenceMatch.Groups['fence'].Value

    if ($currentFenceMarker[0] -eq '`' -and $fenceMatch.Groups['info'].Value.Contains('`')) {
        return $null
    }

    return $currentFenceMarker
}

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
        $inFence = $false
        $fenceMarkerCharacter = $null
        $fenceMarkerLength = 0

        foreach ($line in $lines) {
            if ($inFence) {
                $fenceMatch = [regex]::Match($line, $fencedCodeBlockClosingPattern)

                if ($fenceMatch.Success) {
                    $currentFenceMarker = $fenceMatch.Groups['fence'].Value
                    $currentFenceMarkerCharacter = $currentFenceMarker[0]

                    if ($currentFenceMarkerCharacter -eq $fenceMarkerCharacter -and $currentFenceMarker.Length -ge $fenceMarkerLength) {
                        $inFence = $false
                        $fenceMarkerCharacter = $null
                        $fenceMarkerLength = 0
                    }
                }

                continue
            }

            $currentFenceMarker = Get-FencedCodeBlockOpeningMarker -Line $line

            if ($null -ne $currentFenceMarker) {
                $inFence = $true
                $fenceMarkerCharacter = $currentFenceMarker[0]
                $fenceMarkerLength = $currentFenceMarker.Length
                continue
            }

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

        if ($inFence) {
            Write-Warning "Unterminated fenced code block in $($file.FullName); some headings may have been skipped during indexing."
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
    $inFence = $false
    $fenceMarkerCharacter = $null
    $fenceMarkerLength = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNumber = $i + 1
        $isFenceLine = $false

        if ($inFence) {
            $fenceMatch = [regex]::Match($line, $fencedCodeBlockClosingPattern)

            if ($fenceMatch.Success) {
                $currentFenceMarker = $fenceMatch.Groups['fence'].Value
                $currentFenceMarkerCharacter = $currentFenceMarker[0]

                if ($currentFenceMarkerCharacter -eq $fenceMarkerCharacter -and $currentFenceMarker.Length -ge $fenceMarkerLength) {
                    $isFenceLine = $true
                    $inFence = $false
                    $fenceMarkerCharacter = $null
                    $fenceMarkerLength = 0
                }
            }
        }
        else {
            $currentFenceMarker = Get-FencedCodeBlockOpeningMarker -Line $line

            if ($null -ne $currentFenceMarker) {
                $isFenceLine = $true
                $inFence = $true
                $fenceMarkerCharacter = $currentFenceMarker[0]
                $fenceMarkerLength = $currentFenceMarker.Length
            }
        }

        if (-not $inFence -and -not $isFenceLine) {
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

        if ($line -match $brokenAnchorAllowlistMarker) {
            continue
        }

        foreach ($match in [regex]::Matches($line, $anchoredLinkPattern)) {
            $anchor = $match.Groups['anchor'].Value
            $targetFile = $match.Groups['targetFile'].Value

            if (-not [string]::IsNullOrEmpty($targetFile)) {
                $queryIndex = $targetFile.IndexOf('?')

                if ($queryIndex -ge 0) {
                    $targetFile = $targetFile.Substring(0, $queryIndex)
                }

                if ($targetFile.Contains('://') -or $targetFile -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
                    continue
                }

                if ($targetFile -notmatch '^[A-Za-z0-9._-]+\.md$') {
                    continue
                }
            }

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

$packagedDocNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($projectFile in Get-ChildItem -Path $RepositoryRoot -Filter '*.csproj' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
    $projectText = Get-Content -LiteralPath $projectFile.FullName -Raw

    foreach ($refNameMatch in [regex]::Matches($projectText, '<TrellisApiRefName>\s*(?<name>[^<\s]+)\s*</TrellisApiRefName>')) {
        [void] $packagedDocNames.Add("trellis-api-$($refNameMatch.Groups['name'].Value).md")
    }
}

# Cross-cutting references are not owned by any single package, so they are packed by
# literal path in Directory.Build.targets rather than via TrellisApiRefName.
$directoryBuildTargetsPath = Join-Path $RepositoryRoot 'Directory.Build.targets'

if (Test-Path -LiteralPath $directoryBuildTargetsPath) {
    $targetsText = Get-Content -LiteralPath $directoryBuildTargetsPath -Raw

    foreach ($packedMatch in [regex]::Matches($targetsText, 'api_reference[\\/](?<file>[A-Za-z0-9._-]+\.md)')) {
        [void] $packagedDocNames.Add($packedMatch.Groups['file'].Value)
    }
}

# Generated or repo-internal reports live alongside the references but are deliberately
# not shipped to consumers, so they are exempt from the payload requirement.
$unshippedDocAllowlist = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @('completeness-report.md'),
    [System.StringComparer]::OrdinalIgnoreCase)

foreach ($file in $markdownFiles) {
    if ($packagedDocNames.Contains($file.Name) -or $unshippedDocAllowlist.Contains($file.Name)) {
        continue
    }

    Write-Host "$($file.FullName)(1,1): error TRLDOC004: '$($file.Name)' is not shipped in any NuGet package's trellis/ payload, so consumers never receive it. Add a <TrellisApiRefName> to the owning package, or pack it as a cross-cutting reference in Directory.Build.targets."
    $failed = $true
}

# TRLDOC006 - every cookbook recipe must be pinned by a compiled snippet in
# Examples/CookbookSnippets. Recipes are the most-copied code in the doc set, so an
# unpinned recipe is the shape most likely to rot into code that does not compile.
$cookbookPath = Join-Path $RepositoryRoot 'docs/docfx_project/api_reference/trellis-api-cookbook.md'
$snippetDirectory = Join-Path $RepositoryRoot 'Examples/CookbookSnippets'

if (-not (Test-Path -LiteralPath $cookbookPath)) {
    Write-Host "$cookbookPath(1,1): error TRLDOC006: The cookbook is missing, so recipe snippet coverage cannot be verified. Restore the file or update the path in this script."
    $failed = $true
}
elseif (-not (Test-Path -LiteralPath $snippetDirectory)) {
    Write-Host "$snippetDirectory(1,1): error TRLDOC006: The cookbook snippet project is missing, so no recipe is compiler-verified. Restore Examples/CookbookSnippets or update the path in this script."
    $failed = $true
}
else {
    $snippetNumbers = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($snippetFile in Get-ChildItem -LiteralPath $snippetDirectory -Filter 'Recipe*.cs' -File) {
        $snippetMatch = [regex]::Match($snippetFile.Name, '^Recipe(?<number>\d+)_')

        if ($snippetMatch.Success) {
            [void] $snippetNumbers.Add([int] $snippetMatch.Groups['number'].Value)
        }
    }

    $cookbookLines = Get-Content -LiteralPath $cookbookPath

    # TRLDOC007 - every live recipe must be reachable from the Patterns Index. Agents are
    # instructed to hold only the index resident and load recipe bodies on demand, so a
    # recipe the index never links to is a recipe no agent will ever find.
    $patternsIndexStart = -1
    $patternsIndexEnd = $cookbookLines.Count

    for ($scan = 0; $scan -lt $cookbookLines.Count; $scan++) {
        if ($patternsIndexStart -lt 0) {
            if ($cookbookLines[$scan] -match '^##\s+Patterns Index\s*$') {
                $patternsIndexStart = $scan
            }

            continue
        }

        if ($cookbookLines[$scan] -match '^##\s') {
            $patternsIndexEnd = $scan
            break
        }
    }

    $indexedRecipes = [System.Collections.Generic.HashSet[int]]::new()

    if ($patternsIndexStart -lt 0) {
        Write-Host "$cookbookPath(1,1): error TRLDOC007: The cookbook has no '## Patterns Index' section, so recipe reachability cannot be verified. Restore the section or update the heading in this script."
        $failed = $true
    }
    else {
        $patternsIndexText = ($cookbookLines[$patternsIndexStart..($patternsIndexEnd - 1)] -join "`n")

        foreach ($anchorMatch in [regex]::Matches($patternsIndexText, '#recipe-(?<number>\d+)')) {
            [void] $indexedRecipes.Add([int] $anchorMatch.Groups['number'].Value)
        }
    }

    for ($index = 0; $index -lt $cookbookLines.Count; $index++) {
        $headingMatch = [regex]::Match($cookbookLines[$index], '^##\s+Recipe\s+(?<number>\d+)\s+(?<rest>.*)$')

        if (-not $headingMatch.Success) {
            continue
        }

        # Retired recipes keep their heading so existing anchors and cross-references
        # stay valid, but they intentionally carry no code to pin. Match only the exact
        # marker, so a live recipe whose title merely mentions the word is still gated.
        if ($headingMatch.Groups['rest'].Value -match '\*\(retired\)\*') {
            continue
        }

        $recipeNumber = [int] $headingMatch.Groups['number'].Value

        if (-not $snippetNumbers.Contains($recipeNumber)) {
            Write-Host "$cookbookPath($($index + 1),1): error TRLDOC006: Recipe $recipeNumber has no compiled snippet. Add Examples/CookbookSnippets/Recipe$('{0:D2}' -f $recipeNumber)_<Name>.cs so the recipe's code is compiler-verified."
            $failed = $true
        }

        if ($patternsIndexStart -ge 0 -and -not $indexedRecipes.Contains($recipeNumber)) {
            Write-Host "$cookbookPath($($index + 1),1): error TRLDOC007: Recipe $recipeNumber is not reachable from the Patterns Index. Add a task-phrased row linking to #recipe-$recipeNumber-... so agents that load only the index can discover it."
            $failed = $true
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "API reference lint passed: scanned $($markdownFiles.Count) markdown files."