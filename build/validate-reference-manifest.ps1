<#
.SYNOPSIS
    Validates a packed Trellis nupkg's guidance against the AgentDocs contract and the source documents.
.DESCRIPTION
    Trellis.Core is the only package that ships guidance. It must carry a manifest whose documents
    match the packed bytes, whose usage/description match each document's front matter, and which
    declares exactly one required document (the self-contained router). Every other package must ship
    no manifest, no trellis/ documents and no consumer-side build targets.
.OUTPUTS
    PASS <package> manifest schemaVersion=1 documents=<n> required=<n> onDemand=<n> cohort=<n>
    PASS <package> <path> sha256=<hash>
    PASS <package> ships no guidance
    FAIL <package> <reason> (and a nonzero exit status)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Package,
    [Parameter(Mandatory)][string] $Project,
    [Parameter(Mandatory)][string] $RepositoryRoot,
    [switch] $Quiet
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$name = [System.IO.Path]::GetFileName($Package)
$zip = $null

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
            if ($value -match '^"(?<inner>.*)"$') { return $Matches['inner'] }
            return $value
        }
    }
    return $null
}

try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Package)
    $entries = @($zip.Entries)

    # No package may bring back a consumer-side build hook: guidance is installed only by the
    # opt-in AgentDocs tool, never by restore or build.
    $consumerTargets = @($entries | Where-Object { $_.FullName -match '^(build|buildTransitive)/[^/]+\.targets$' } |
        Where-Object {
            $reader = [System.IO.StreamReader]::new($_.Open())
            try { $reader.ReadToEnd() -match 'TrellisApiReference|_CopyTrellisApiReference|TrellisSyncApiReference|_WarnTrellisApiReferenceCopyLogicMissing' }
            finally { $reader.Dispose() }
        })
    if ($consumerTargets.Count) { throw "consumer-side reference target packed: $($consumerTargets[0].FullName)" }
    $legacyPayload = @($entries | Where-Object { $_.FullName -match '^(build|buildTransitive)/Trellis\.ApiReference\.Payload\.targets$' })
    if ($legacyPayload.Count) { throw "legacy payload target packed: $($legacyPayload[0].FullName)" }

    [xml]$projectXml = Get-Content -LiteralPath $Project -Raw
    $shipsSet = [string]$projectXml.SelectSingleNode('//TrellisShipsApiReferenceSet')?.InnerText -eq 'true'
    $manifests = @($entries | Where-Object FullName -CEQ 'guidance/reference-manifest.json')
    $packedDocs = @($entries | Where-Object { $_.FullName -like 'trellis/*' })

    if (-not $shipsSet) {
        if ($manifests.Count -or $packedDocs.Count) {
            throw 'only Trellis.Core ships guidance; this package packed a manifest or trellis/ documents'
        }
        if (-not $Quiet) { Write-Host "PASS $name ships no guidance" }
        return
    }

    if ($manifests.Count -ne 1) { throw "expected one guidance/reference-manifest.json; found $($manifests.Count)" }
    $stream = $manifests[0].Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($manifest.schemaVersion -cne 1) { throw "unsupported schemaVersion $($manifest.schemaVersion)" }
    if ($null -eq $manifest.documents) { throw 'documents is required' }
    foreach ($field in $manifest.Keys) {
        if ($field -cnotin @('schemaVersion', 'documents', 'publisherMetadata')) {
            throw "unexpected manifest field: $field"
        }
    }
    if ($manifest.publisherMetadata -isnot [System.Collections.IDictionary] -or
        $manifest.publisherMetadata.'org.trellis' -isnot [System.Collections.IDictionary]) {
        throw 'missing namespaced org.trellis publisherMetadata'
    }

    $docsRoot = Join-Path (Join-Path (Join-Path $RepositoryRoot 'docs') 'docfx_project') 'api_reference'
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $required = @()
    $onDemand = 0
    foreach ($doc in $manifest.documents) {
        foreach ($field in $doc.Keys) {
            if ($field -cnotin @('path', 'sha256', 'usage', 'description')) { throw "unexpected document field: $field" }
        }
        $path = [string]$doc.path
        if ($path -notmatch '^trellis/[^/\\]+\.md$' -or $path.Contains('..')) { throw "invalid path: $path" }
        if ($doc.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "invalid sha256 for $path" }
        if (-not $seen.Add($path.Normalize([System.Text.NormalizationForm]::FormC))) {
            throw "duplicate document path: $path"
        }
        if ($doc.usage -cnotin @('required', 'onDemand', 'supporting')) { throw "invalid usage for ${path}: $($doc.usage)" }
        $description = if ($doc.ContainsKey('description')) { [string]$doc.description } else { '' }
        if ($doc.usage -cne 'supporting' -and -not $description) { throw "$($doc.usage) document $path has no description" }
        if ($description) {
            if (@($description.EnumerateRunes()).Count -gt 200) { throw "description of $path exceeds 200 characters" }
            if ($description -match '[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]') { throw "description of $path contains control or line-separator characters" }
        }
        if ($doc.usage -ceq 'required') { $required += $path }
        if ($doc.usage -ceq 'onDemand') { $onDemand++ }

        $packedEntry = @($entries | Where-Object FullName -CEQ $path)
        if ($packedEntry.Count -ne 1) { throw "missing or duplicate packed document: $path" }
        $bytes = [System.IO.MemoryStream]::new()
        $inputStream = $packedEntry[0].Open()
        try { $inputStream.CopyTo($bytes) } finally { $inputStream.Dispose() }
        $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes.ToArray())).ToLowerInvariant()
        $bytes.Dispose()
        if ($doc.sha256 -cne $hash) { throw "sha256 mismatch for $path (expected $($doc.sha256), actual $hash)" }

        # Usage and description come from the document's own front matter; a mismatch means the
        # manifest was not generated from the shipped source.
        $lines = @(Get-Content -LiteralPath (Join-Path $docsRoot ([System.IO.Path]::GetFileName($path))))
        if ((Get-FrontMatterValue $lines 'agent_usage') -cne $doc.usage) { throw "usage of $path differs from its front matter" }
        $sourceDescription = Get-FrontMatterValue $lines 'agent_description'
        $expectedDescription = if ($sourceDescription) { $sourceDescription.Normalize([System.Text.NormalizationForm]::FormC) } else { '' }
        if ($expectedDescription -cne $description) {
            throw "description of $path differs from its front matter"
        }
        if (-not $Quiet) { Write-Host "PASS $name $path sha256=$hash" }
    }
    if ($required.Count -ne 1 -or $required[0] -cne 'trellis/trellis-start-here.md') {
        throw "exactly one required document, trellis/trellis-start-here.md, is expected (found: $($required -join ', '))"
    }
    if ($packedDocs.Count -ne $seen.Count) { throw "packed documents $($packedDocs.Count) != listed $($seen.Count)" }

    $expected = @(Get-ChildItem -Path $docsRoot -Filter '*.md' -File |
        Where-Object Name -NE 'completeness-report.md' | ForEach-Object { "trellis/$($_.Name)" })
    if (@($expected | Where-Object { -not $seen.Contains($_) }).Count -gt 0 -or $expected.Count -ne $seen.Count) {
        throw 'manifest document set differs from the source reference set'
    }

    $cohort = @(Get-ChildItem -LiteralPath $RepositoryRoot -Directory -Filter 'Trellis.*' |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'src') -Filter '*.csproj' -File -ErrorAction SilentlyContinue } |
        Where-Object {
            [xml]$candidate = Get-Content -LiteralPath $_.FullName -Raw
            $candidate.SelectSingleNode('//IsPackable')?.InnerText -ne 'false' -and
            $candidate.SelectSingleNode('//PackAsTool')?.InnerText -ne 'true'
        } |
        ForEach-Object { $_.BaseName } | Sort-Object -Unique)
    $declared = @($manifest.publisherMetadata.'org.trellis'.lockstepCohort)
    if ($declared.Count -ne $cohort.Count -or @($cohort | Where-Object { $declared -cnotcontains $_ }).Count -gt 0) {
        throw 'Core authoritative lockstep cohort differs from packable-project set'
    }
    if (-not $Quiet) {
        Write-Host "PASS $name manifest schemaVersion=1 documents=$($seen.Count) required=$($required.Count) onDemand=$onDemand cohort=$($cohort.Count)"
    }
}
catch {
    Write-Output "FAIL $name $($_.Exception.Message)"
    exit 1
}
finally {
    if ($zip) { $zip.Dispose() }
}
