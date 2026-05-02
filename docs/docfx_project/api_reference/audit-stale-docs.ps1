#Requires -Version 7

param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
)

$ErrorActionPreference = 'Stop'

Push-Location $RepositoryRoot
try {
    $trackedFiles = @(git ls-files)
    $untrackedFiles = @(git ls-files --others --exclude-standard)
    $filesToScan = @($trackedFiles + $untrackedFiles | Sort-Object -Unique)

    $includedExtensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @('.cs', '.csproj', '.json', '.md', '.props', '.ps1', '.targets', '.t4', '.tt', '.yaml', '.yml')) {
        [void] $includedExtensions.Add($extension)
    }

    $allowlistedPathPatterns = @(
        '^CHANGELOG\.md$',
        '^MIGRATION_v3\.md$',
        '^docs/docfx_project/articles/migration\.md$',
        '^docs/docfx_project/api_reference/audit-stale-docs\.ps1$',
        '^docs/docfx_project/adr/',
        '^Trellis\.Asp/SAMPLES\.md$',
        '^Trellis\.Core/src/Result/Unit\.cs$',
        '(^|/)tests/',
        '(^|/)generator-tests/',
        '^Examples/[^/]+\.Tests/'
    )

    $allowlistedLinePatterns = @(
        'stale-doc-ok',
        'v1-stale-ok',
        '\bunit test\b',
        '\bUnit-test\b',
        '\bUnit of Work\b',
        '\bUnitPrice\b',
        'RangeNotSatisfiable\(long CompleteLength, string Unit',
        'Error\.RangeNotSatisfiable.*Unit = "bytes"',
        'Content-Range: \{Unit\}',
        '<param name="Unit">',
        'bindingContext\.Result = emptyResult\.Value',
        'range unit'
    )

    $stalePatterns = @(
        @{ Pattern = '\bnon-generic\s+non-generic\b'; Message = 'Remove duplicated non-generic wording.' },
        @{ Pattern = '\bAPI\s+\.'; Message = 'Remove stray space before punctuation.' },
        @{ Pattern = '^\s*///,'; Message = 'Fix XML doc punctuation after line wrapping.' },
        @{ Pattern = '\bvoid/No-payload\b'; Message = 'Use no-payload wording without mixed casing/slashes.' },
        @{ Pattern = 'Error\.Equals\(\.\.\.\) compares \*\*only the error code\*\*'; Message = 'Error equality is value-based; compare Code for category-only checks.' },
        @{ Pattern = '\bTrellis\.Results\b'; Message = 'Trellis.Results is not a current package; use Trellis.Core unless this is historical migration content.' },
        @{ Pattern = '\bTrellis\.DomainDrivenDesign\b'; Message = 'Trellis.DomainDrivenDesign is not a current package; use Trellis.Core unless this is historical migration content.' },
        @{ Pattern = '\b(ToActionResult|ToActionResultAsync|ToHttpResult|ToHttpResultAsync|ToCreatedAtActionResult|ToCreatedAtRouteHttpResult|ToCreatedHttpResult|ToUpdatedActionResult|ToUpdatedHttpResult|ToPagedActionResult|ToPagedHttpResult)\b'; Message = 'Use ToHttpResponse(Async) and AsActionResult<T>(Async) for current ASP response mapping.' },
        @{ Pattern = '\bResult\.Success\s*[(<]'; Message = 'Result.Success is removed; use Result.Ok(...).' },
        @{ Pattern = '\bResult\.Failure\s*[(<]'; Message = 'Result.Failure is removed; use Result.Fail<T>(...).' },
        @{ Pattern = '\bResult\.SuccessIf(?:Async)?\b'; Message = 'Result.SuccessIf is removed; use a ternary with Result.Ok/Fail.' },
        @{ Pattern = '\bResult\.FailureIf(?:Async)?\b'; Message = 'Result.FailureIf is removed; use a ternary with Result.Ok/Fail.' },
        @{ Pattern = '(?-i:\bError\.(?:Validation|Domain|Failure)\b)'; Message = 'Error.Validation/Error.Domain/Error.Failure are not current API cases; use the closed-ADT error cases.' },
        @{ Pattern = '(?-i:\b(?:Validation|NotFound|Conflict|Unauthorized|Forbidden|Unexpected)Error\b)'; Message = 'Old concrete error subclasses are not current API types; use Error.<Case> records.' },
        @{ Pattern = '\bFinally\s*\('; Message = 'Finally is removed; use Match(onSuccess:, onFailure:) as the terminal verb.' },
        @{ Pattern = '(?-i:(?<!\.)\b(?:result|[a-z][A-Za-z0-9_]*Result|r)\.Value\b)'; Message = 'Result<T>.Value was removed; use TryGetValue, Match, Deconstruct, or GetValueOrDefault.' },
        @{ Pattern = '\bADR-00[0-9]+\b'; Message = 'Current-facing docs should describe current behavior, not design-process ADR references.' },
        @{ Pattern = '\bv2 redesign\b'; Message = 'Current-facing docs should not reference completed redesign process wording.' },
        @{ Pattern = '\bPhase\s+[0-9][A-Za-z]?\b'; Message = 'Current-facing docs should not reference completed phase process wording.' },
        @{ Pattern = '\bsince Phase\b'; Message = 'Current-facing docs should describe current package contents directly.' },
        @{ Pattern = '\bv2 pipeline\b'; Message = 'Current-facing docs should use current pipeline wording.' },
        @{ Pattern = '\bv2 replacement\b'; Message = 'Current-facing docs should use current API wording.' },
        @{ Pattern = '\bv2 design\b'; Message = 'Current-facing docs should not reference completed redesign process wording.' },
        @{ Pattern = '\bremoved in v2\b'; Message = 'Current-facing docs should say removed from the current API.' },
        @{ Pattern = '\bdeleted in v2\b'; Message = 'Current-facing docs should say deleted from the current API.' },
        @{ Pattern = '\bfrom v2 onward\b'; Message = 'Current-facing docs should state the canonical behavior directly.' },
        @{ Pattern = '\bpre-V2\b'; Message = 'Current-facing docs should use previous/current API wording.' },
        @{ Pattern = '\bTrellis V2\b'; Message = 'Current-facing docs should use current Trellis wording.' },
        @{ Pattern = '\bv2 Mediator pipeline\b'; Message = 'Current-facing docs should use current pipeline wording.' },
        @{ Pattern = '\bv2 closed-ADT\b'; Message = 'Current-facing docs should describe the current closed ADT directly.' }
    )

    $hits = New-Object System.Collections.Generic.List[string]

    foreach ($file in $filesToScan) {
        $normalizedPath = $file -replace '\\', '/'
        $extension = [System.IO.Path]::GetExtension($normalizedPath)

        if (-not $includedExtensions.Contains($extension)) {
            continue
        }

        $pathAllowed = $false
        foreach ($pathPattern in $allowlistedPathPatterns) {
            if ($normalizedPath -match $pathPattern) {
                $pathAllowed = $true
                break
            }
        }

        if ($pathAllowed) {
            continue
        }

        if (-not (Test-Path -LiteralPath $file)) {
            continue
        }

        $lines = @(Get-Content -LiteralPath $file)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]

            $lineAllowed = $false
            foreach ($linePattern in $allowlistedLinePatterns) {
                if ($line -match $linePattern) {
                    $lineAllowed = $true
                    break
                }
            }

            if ($lineAllowed) {
                continue
            }

            foreach ($stalePattern in $stalePatterns) {
                if ($line -match $stalePattern.Pattern) {
                    $lineNumber = $i + 1
                    $hits.Add("${normalizedPath}:${lineNumber}: $($stalePattern.Message)`n  $line")
                }
            }
        }
    }

    if ($hits.Count -gt 0) {
        Write-Host "Found stale current-facing documentation references:"
        foreach ($hit in $hits) {
            Write-Host $hit
        }
        exit 1
    }

    Write-Host 'No stale current-facing documentation references found.'
}
finally {
    Pop-Location
}
