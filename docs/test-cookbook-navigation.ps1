#Requires -Version 7
<#
.SYNOPSIS
    Checks recipe navigation in the rendered DocFX site.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$referenceDirectory = Join-Path $RepositoryRoot 'docs' 'docfx_project' 'api_reference'
$siteDirectory = Join-Path $RepositoryRoot 'docs' 'docfx_project' '_site' 'api_reference'
$cookbookPath = Join-Path $referenceDirectory 'trellis-api-cookbook.md'
$htmlPath = Join-Path $siteDirectory 'trellis-api-cookbook.html'
$tocPath = Join-Path $siteDirectory 'toc.json'
$tocHtmlPath = Join-Path $siteDirectory 'toc.html'

foreach ($path in @($cookbookPath, $htmlPath, $tocPath, $tocHtmlPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Cookbook navigation input is missing: $path. Build the DocFX site before running this check."
    }
}

function Get-NavigationEntries {
    param([object[]] $Items)

    foreach ($item in $Items) {
        $item
        if ($item.PSObject.Properties['items']) {
            Get-NavigationEntries -Items $item.items
        }
    }
}

$liveRecipes = @(
    Get-Content -LiteralPath $cookbookPath |
        Where-Object { $_ -match '^## Recipe \d+\s' -and $_ -notmatch '\*\(retired\)\*' } |
        ForEach-Object { [int] [regex]::Match($_, '^## Recipe (?<number>\d+)').Groups['number'].Value }
)
if ($liveRecipes.Count -eq 0) {
    throw "No live recipe headings were found in $cookbookPath."
}

$toc = Get-Content -LiteralPath $tocPath -Raw | ConvertFrom-Json -Depth 100
$cookbook = @($toc.items | Where-Object {
    $_.PSObject.Properties['href'] -and $_.href -eq 'trellis-api-cookbook.html'
})
if ($cookbook.Count -ne 1 -or -not $cookbook[0].PSObject.Properties['items']) {
    throw 'The DocFX TOC must contain one Cookbook entry with grouped recipe navigation.'
}

$groups = @($cookbook[0].items | Where-Object { $_.PSObject.Properties['items'] })
if ($groups.Count -lt 2 -or $groups.Count -ne @($cookbook[0].items).Count) {
    throw 'Cookbook recipes must be grouped by topic in the DocFX TOC.'
}

$links = @(Get-NavigationEntries -Items $cookbook[0].items | Where-Object {
    $_.PSObject.Properties['href']
})
$html = Get-Content -LiteralPath $htmlPath -Raw
$tocHtml = Get-Content -LiteralPath $tocHtmlPath -Raw
$headings = @([regex]::Matches($html, '<h2\b[^>]*\bid="(?<anchor>recipe-(?<number>\d+)-[^"]+)"'))
$failures = [System.Collections.Generic.List[string]]::new()
$expectedTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$jsonTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$htmlTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($anchor in [regex]::Matches($tocHtml, '<a\b[^>]*\bhref="(?<href>trellis-api-cookbook\.html#[^"]+)"')) {
    [void] $htmlTargets.Add([System.Net.WebUtility]::HtmlDecode($anchor.Groups['href'].Value))
}

foreach ($number in $liveRecipes) {
    $heading = @($headings | Where-Object { [int] $_.Groups['number'].Value -eq $number })
    if ($heading.Count -ne 1) {
        $failures.Add("Recipe $number must have exactly one rendered heading; found $($heading.Count).")
        continue
    }

    $href = 'trellis-api-cookbook.html#' + $heading[0].Groups['anchor'].Value
    [void] $expectedTargets.Add($href)
    $recipeLinks = @($links | Where-Object { $_.href -ceq $href })
    if ($recipeLinks.Count -ne 1) {
        $failures.Add("Recipe $number must have exactly one TOC link to '$href'; found $($recipeLinks.Count).")
    }
    foreach ($recipeLink in $recipeLinks) {
        if (-not $recipeLink.PSObject.Properties['name'] -or $recipeLink.name -cnotlike "$number. *") {
            $failures.Add("Recipe $number TOC label must start with '$number. '.")
        }
    }
}

foreach ($link in $links) {
    [void] $jsonTargets.Add($link.href)
    if (-not $expectedTargets.Contains($link.href)) {
        $failures.Add("Unexpected cookbook navigation target: '$($link.href)'. Link only to live recipes in the canonical cookbook.")
    }
    if (-not $htmlTargets.Contains($link.href)) {
        $failures.Add("The rendered HTML TOC is missing '$($link.href)'.")
    }
}

foreach ($target in $htmlTargets) {
    if (-not $jsonTargets.Contains($target)) {
        $failures.Add("The rendered HTML TOC contains unexpected cookbook target '$target'.")
    }
}

if ($failures.Count -gt 0) {
    throw "Cookbook navigation failed:`n$($failures -join "`n")"
}

Write-Host "Cookbook navigation passed: $($liveRecipes.Count) live recipes in $($groups.Count) topic groups, with rendered HTML links."
