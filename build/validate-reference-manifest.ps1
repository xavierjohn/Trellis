<#
.SYNOPSIS
    Validates the actual nupkg manifest against its packed document bytes and source graph.
.OUTPUTS
    PASS <package> manifest schemaVersion=1 documents=<n> entryPoints=<n> cohort=<n>
    PASS <package> <path> sha256=<hash>
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
try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Package)
    $entries = @($zip.Entries)
    $legacyPayload = @($entries | Where-Object {
        $_.FullName -match '^(build|buildTransitive)/Trellis\.ApiReference\.Payload\.targets$'
    })
    if ($legacyPayload.Count) { throw "legacy payload target packed: $($legacyPayload[0].FullName)" }
    $manifests = @($entries | Where-Object FullName -CEQ 'guidance/reference-manifest.json')
    if ($manifests.Count -ne 1) { throw "expected one guidance/reference-manifest.json; found $($manifests.Count)" }
    $stream = $manifests[0].Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($manifest.schemaVersion -cne 1) { throw "unsupported schemaVersion $($manifest.schemaVersion)" }
    if ($null -eq $manifest.documents -or $null -eq $manifest.entryPoints) {
        throw 'documents and entryPoints are required'
    }
    foreach ($field in $manifest.Keys) {
        if ($field -cnotin @('schemaVersion', 'documents', 'entryPoints', 'publisherMetadata')) {
            throw "unexpected manifest field: $field"
        }
    }
    if ($manifest.publisherMetadata -isnot [System.Collections.IDictionary] -or
        $manifest.publisherMetadata.'org.trellis' -isnot [System.Collections.IDictionary]) {
        throw 'missing namespaced org.trellis publisherMetadata'
    }
    if ($manifest.documents.Count -gt 0 -and $manifest.entryPoints.Count -eq 0) {
        throw 'nonempty documents require an entry point'
    }
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($doc in $manifest.documents) {
        foreach ($field in $doc.Keys) {
            if ($field -cnotin @('path', 'sha256', 'role')) { throw "unexpected document field: $field" }
        }
        $path = [string]$doc.path
        if ($path -notmatch '^trellis/[^/\\]+\.md$' -or $path.Contains('..')) { throw "invalid path: $path" }
        if ($doc.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "invalid sha256 for $path" }
        if (-not $seen.Add($path.Normalize([System.Text.NormalizationForm]::FormC))) {
            throw "duplicate document path: $path"
        }
        $matches = @($entries | Where-Object FullName -CEQ $path)
        if ($matches.Count -ne 1) { throw "missing or duplicate packed document: $path" }
        $bytes = [System.IO.MemoryStream]::new()
        $inputStream = $matches[0].Open()
        try { $inputStream.CopyTo($bytes) } finally { $inputStream.Dispose() }
        $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes.ToArray())).ToLowerInvariant()
        $bytes.Dispose()
        if ($doc.sha256 -cne $hash) { throw "sha256 mismatch for $path (expected $($doc.sha256), actual $hash)" }
        if (-not $Quiet) { Write-Host "PASS $name $path sha256=$hash" }
    }
    foreach ($point in $manifest.entryPoints) {
        if (@($manifest.documents | Where-Object { $_.path -ceq $point }).Count -ne 1) {
            throw "entryPoint not listed exactly once in documents: $point"
        }
    }
    $packedDocs = @($entries | Where-Object { $_.FullName -like 'trellis/*.md' })
    if ($packedDocs.Count -ne $seen.Count) { throw "packed documents $($packedDocs.Count) != listed $($seen.Count)" }
    [xml]$projectXml = Get-Content -LiteralPath $Project -Raw
    $mode = [string]$projectXml.SelectSingleNode('//TrellisShipsApiReferenceSet')?.InnerText
    $own = [string]$projectXml.SelectSingleNode('//TrellisShipsOwnApiReference')?.InnerText
    $expected = @(& { if ($mode -eq 'true') {
        @(Get-ChildItem -Path (Join-Path $RepositoryRoot 'docs\docfx_project\api_reference') -Filter '*.md' -File |
            Where-Object Name -NE 'completeness-report.md' | ForEach-Object { "trellis/$($_.Name)" })
    }
    elseif ($own -eq 'true') {
        $ref = [string]$projectXml.SelectSingleNode('//TrellisApiRefName')?.InnerText
        @("trellis/trellis-api-$ref.md")
    }
    else { @() } })
    if (@($expected | Where-Object { -not $seen.Contains($_) }).Count -gt 0 -or $expected.Count -ne $seen.Count) {
        throw "manifest document set differs from source package declaration"
    }
    $cohort = @(Get-ChildItem -LiteralPath $RepositoryRoot -Directory -Filter 'Trellis.*' |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'src') -Filter '*.csproj' -File -ErrorAction SilentlyContinue } |
        Where-Object {
            [xml]$candidate = Get-Content -LiteralPath $_.FullName -Raw
            $candidate.SelectSingleNode('//IsPackable')?.InnerText -ne 'false' -and
            $candidate.SelectSingleNode('//PackAsTool')?.InnerText -ne 'true'
        } |
        ForEach-Object { $_.BaseName } | Sort-Object -Unique)
    if ($mode -eq 'true') {
        if ($manifest.entryPoints.Count -ne 1 -or
            $manifest.entryPoints[0] -cne 'trellis/trellis-start-here.md') {
            throw 'Core must declare the packaged router as its entry point'
        }
        if (@($manifest.publisherMetadata.'org.trellis'.lockstepCohort).Count -ne $cohort.Count -or
            @($cohort | Where-Object { $manifest.publisherMetadata.'org.trellis'.lockstepCohort -cnotcontains $_ }).Count -gt 0) {
            throw 'Core authoritative lockstep cohort differs from packable-project set'
        }
    }
    elseif ($manifest.publisherMetadata.'org.trellis'.lockstep -cne $true) { throw 'missing Trellis lockstep membership' }
    $legacy = @($entries | Where-Object {
        if ($_.FullName -notmatch '^(build|buildTransitive)/[^/]+\.targets$') { return $false }
        $reader = [System.IO.StreamReader]::new($_.Open())
        try { return $reader.ReadToEnd() -match '_CopyTrellisApiReference|TrellisSyncApiReference|_WarnTrellisApiReferenceCopyLogicMissing' }
        finally { $reader.Dispose() }
    })
    if ($legacy.Count) { throw "legacy copy or warning target packed: $($legacy[0].FullName)" }
    if (-not $Quiet) {
        Write-Host "PASS $name manifest schemaVersion=1 documents=$($seen.Count) entryPoints=$($manifest.entryPoints.Count) cohort=$(if ($mode -eq 'true') { $cohort.Count } else { 0 })"
    }
}
catch {
    Write-Output "FAIL $name $($_.Exception.Message)"
    exit 1
}
finally {
    if ($zip) { $zip.Dispose() }
}
