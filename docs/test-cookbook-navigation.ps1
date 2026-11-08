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
$tocPath = Join-Path $siteDirectory 'toc.json'
$tocHtmlPath = Join-Path $siteDirectory 'toc.html'

foreach ($path in @($tocPath, $tocHtmlPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Recipe navigation input is missing: $path. Build the DocFX site before running this check."
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

# Each recipe is its own document, skills/recipe-NN-<topic>.md.
$recipeFiles = @(Get-ChildItem -LiteralPath (Join-Path $referenceDirectory 'skills') -Filter 'recipe-*.md' -File | Sort-Object Name)
if ($recipeFiles.Count -eq 0) {
    throw "No recipe documents (recipe-*.md) were found in $referenceDirectory."
}

$toc = Get-Content -LiteralPath $tocPath -Raw | ConvertFrom-Json -Depth 100
$recipes = @($toc.items | Where-Object {
    $_.PSObject.Properties['name'] -and $_.name -ceq 'Recipes'
})
if ($recipes.Count -ne 1 -or -not $recipes[0].PSObject.Properties['items']) {
    throw 'The DocFX TOC must contain one Recipes entry with grouped recipe navigation.'
}

$groups = @($recipes[0].items | Where-Object { $_.PSObject.Properties['items'] })
if ($groups.Count -lt 2 -or $groups.Count -ne @($recipes[0].items).Count) {
    throw 'Recipes must be grouped by topic in the DocFX TOC.'
}

$links = @(Get-NavigationEntries -Items $recipes[0].items | Where-Object {
    $_.PSObject.Properties['href']
})
$tocHtml = Get-Content -LiteralPath $tocHtmlPath -Raw
$failures = [System.Collections.Generic.List[string]]::new()
$expectedTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$jsonTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$htmlTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($anchor in [regex]::Matches($tocHtml, '<a\b[^>]*\bhref="(?<href>skills/recipe-[^"#]+\.html)"')) {
    [void] $htmlTargets.Add([System.Net.WebUtility]::HtmlDecode($anchor.Groups['href'].Value))
}

foreach ($recipeFile in $recipeFiles) {
    $number = [int] [regex]::Match($recipeFile.Name, '^recipe-(?<number>\d+)-').Groups['number'].Value
    $page = 'skills/' + [System.IO.Path]::ChangeExtension($recipeFile.Name, '.html')
    $pagePath = Join-Path $siteDirectory $page
    if (-not (Test-Path -LiteralPath $pagePath -PathType Leaf)) {
        $failures.Add("Recipe $number has no rendered page '$page'.")
        continue
    }

    $headings = @([regex]::Matches((Get-Content -LiteralPath $pagePath -Raw), "<h1\b[^>]*\bid=`"recipe-$number-[^`"]*`""))
    if ($headings.Count -ne 1) {
        $failures.Add("Recipe $number must have exactly one rendered recipe heading in '$page'; found $($headings.Count).")
    }

    [void] $expectedTargets.Add($page)
    $recipeLinks = @($links | Where-Object { $_.href -ceq $page })
    if ($recipeLinks.Count -ne 1) {
        $failures.Add("Recipe $number must have exactly one TOC link to '$page'; found $($recipeLinks.Count).")
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
        $failures.Add("Unexpected recipe navigation target: '$($link.href)'. Link only to live recipe documents.")
    }
    if (-not $htmlTargets.Contains($link.href)) {
        $failures.Add("The rendered HTML TOC is missing '$($link.href)'.")
    }
}

foreach ($target in $htmlTargets) {
    if (-not $jsonTargets.Contains($target)) {
        $failures.Add("The rendered HTML TOC contains unexpected recipe target '$target'.")
    }
}

if ($failures.Count -gt 0) {
    throw "Recipe navigation failed:`n$($failures -join "`n")"
}

Write-Host "Recipe navigation passed: $($recipeFiles.Count) live recipe documents in $($groups.Count) topic groups, with rendered HTML links."
