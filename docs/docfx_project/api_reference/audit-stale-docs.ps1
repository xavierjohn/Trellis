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
    foreach ($extension in @('.cs', '.md', '.ps1', '.t4', '.tt', '.yaml', '.yml')) {
        [void] $includedExtensions.Add($extension)
    }

    $allowlistedPathPatterns = @(
        '^CHANGELOG\.md$',
        '^MIGRATION_v3\.md$',
        '^docs/docfx_project/api_reference/audit-stale-docs\.ps1$',
        '^docs/docfx_project/adr/',
        '^Trellis\.Core/src/Result/Unit\.cs$',
        '(^|/)tests/',
        '(^|/)generator-tests/'
    )

    $allowlistedLinePatterns = @(
        'stale-doc-ok',
        '\bunit test\b',
        '\bUnit-test\b',
        '\bUnit of Work\b',
        '\bUnitPrice\b',
        'RangeNotSatisfiable\(long CompleteLength, string Unit',
        'Error\.RangeNotSatisfiable.*Unit = "bytes"',
        'Content-Range: \{Unit\}',
        '<param name="Unit">',
        'range unit'
    )

    $stalePatterns = @(
        @{ Pattern = '\bUnit\.Value\b'; Message = 'Unit.Value is not a public API; use Result.Ok() for no-payload success.' },
        @{ Pattern = '\bUnit\.Default\b'; Message = 'Unit.Default is not a public API; use Result.Ok() for no-payload success.' },
        @{ Pattern = '\bnew\s+Unit\s*\('; Message = 'Unit is not a public API; use non-generic Result for no-payload operations.' },
        @{ Pattern = '\bdefault\s*\(\s*Unit\s*\)'; Message = 'Unit is not a public API; use non-generic Result for no-payload operations.' },
        @{ Pattern = '\bResult\s*<\s*Unit\s*>'; Message = 'Result<Unit> is not a public API; use non-generic Result.' },
        @{ Pattern = '\bResult\{Unit\}'; Message = 'Result{Unit} is not a public API; use non-generic Result.' },
        @{ Pattern = '\bpublic\s+record\s+struct\s+Unit\b'; Message = 'Unit is not a public type; do not document it as public API.' },
        @{ Pattern = '\brecord\s+struct\s+Unit\b'; Message = 'Unit is not a public type; do not document it as public API.' },
        @{ Pattern = '\bUnit-shaped\b'; Message = 'Prefer no-payload or void-style Result wording.' },
        @{ Pattern = '\bUnit result\b'; Message = 'Prefer non-generic Result wording.' },
        @{ Pattern = '\bResult of Unit\b'; Message = 'Prefer non-generic Result wording.' },
        @{ Pattern = '\bUnit Results\b'; Message = 'Prefer non-generic Result wording.' },
        @{ Pattern = '\bUnit support\b'; Message = 'Prefer non-generic Result wording.' },
        @{ Pattern = '\bvoid/Unit\b'; Message = 'Prefer no-payload or void-style Result wording.' },
        @{ Pattern = '\bnon-generic\s+non-generic\b'; Message = 'Remove duplicated non-generic wording.' },
        @{ Pattern = '\bAPI\s+\.'; Message = 'Remove stray space before punctuation.' },
        @{ Pattern = '^\s*///,'; Message = 'Fix XML doc punctuation after line wrapping.' },
        @{ Pattern = '\bvoid/No-payload\b'; Message = 'Use no-payload wording without mixed casing/slashes.' },
        @{ Pattern = 'Error\.Equals\(\.\.\.\) compares \*\*only the error code\*\*'; Message = 'Error equality is value-based; compare Code for category-only checks.' },
        @{ Pattern = '\bADR-002\b'; Message = 'Current-facing docs should describe current behavior, not redesign-plan references.' },
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
