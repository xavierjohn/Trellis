#Requires -Version 7
<#
.SYNOPSIS
  LLM-doc-quality smoke test for the Trellis docfx project.

.DESCRIPTION
  Runs four static probes against `docs/docfx_project/articles/*.md`,
  `docs/docfx_project/api_reference/*.md`, and the Trellis package source
  trees. Designed to catch regressions in LLM-targeted documentation
  quality without invoking a model. Runs in seconds and exits non-zero
  on any probe failure so it can gate a CI workflow.

  Probe 1 — Anti-pattern absence
    Searches current-facing documentation for v1 surfaces and other
    patterns the new (v2/v3) surface has obsoleted. A hit indicates a
    user/AI could be misled into copying broken code.

  Probe 2 — Canonical-form presence
    Confirms each v2 canonical pattern is documented in articles/.
    A missing pattern means an LLM has no positive example to imitate.

  Probe 3 — Public-surface coverage
    For every public type defined in `Trellis.*/src/**.cs`, checks that
    its simple name appears at least once across articles/ or
    api_reference/. Reports coverage % and lists uncovered types.
    Threshold is configurable via -SurfaceCoverageThreshold.

  Probe 4 — Frontmatter completeness
    Every .md in articles/ and api_reference/ (excluding the
    auto-generated completeness-report.md) must declare the required
    YAML keys for its collection.

.PARAMETER RepositoryRoot
  Path to the repository root. Defaults to the resolved
  `docs/docfx_project/..` from this script's location.

.PARAMETER SurfaceCoverageThreshold
  Minimum acceptable surface-coverage percentage (Probe 3). Default 70.

.PARAMETER FailOnWarnings
  Treat WARN findings as failures (raises exit code).

.EXAMPLE
  pwsh -NoProfile -File audit-llm-doc-quality.ps1

.EXAMPLE
  pwsh -NoProfile -File audit-llm-doc-quality.ps1 -SurfaceCoverageThreshold 80
#>

param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [int]    $SurfaceCoverageThreshold = 70,
    [switch] $FailOnWarnings
)

$ErrorActionPreference = 'Stop'

# ----------------------------------------------------------------------------
# Configuration
# ----------------------------------------------------------------------------

$ArticlesDir     = Join-Path $RepositoryRoot 'docs/docfx_project/articles'
$ApiReferenceDir = Join-Path $RepositoryRoot 'docs/docfx_project/api_reference'

# Probe 1 file allowlist — files that legitimately document v1 surfaces
# (migration guides) should NOT trigger anti-pattern findings, matching
# the convention used by audit-stale-docs.ps1.
$Probe1FileAllowlist = @(
    'docs/docfx_project/articles/migration.md'
)

# Probe 1: anti-patterns. Each entry: literal-or-regex pattern + reason.
# Patterns scan articles/*.md + api_reference/*.md (current-facing docs only).
# Per-line `v1-stale-ok` marker exempts a single line (matches the existing
# audit-stale-docs.ps1 convention).
$AntiPatterns = @(
    @{ Pattern = '\bError\.(Validation|Domain|Failure)\(';          Reason = 'v1 Error static factory (use new Error.X(...))' },
    @{ Pattern = '\bPage\.Create\(';                                Reason = 'Nonexistent factory (use new Page<T>(...) / Page.Empty<T>())' },
    @{ Pattern = '\bInputPointer\.Append\(';                        Reason = 'Nonexistent method' },
    @{ Pattern = '\bHonorPreconditions\b';                          Reason = 'v1 method name (use EvaluatePreconditions)' },
    @{ Pattern = '\bToActionResultAsync\s*\(\s*this\b';             Reason = 'v1 ASP bridge (use ToHttpResponseAsync(...).AsActionResultAsync<T>())' },
    @{ Pattern = '\b18\s+nested\b';                                 Reason = 'Stale Error ADT count (now 20 nested)' },
    @{ Pattern = '\bResult\.Failure<';                              Reason = 'v1 Result factory (use Result.Fail<T>(...))' }
)

# Probe 2: canonical-form presence. Each pattern must appear ≥ MinHits times
# across articles/*.md (developer-facing). Most need only 1; widely-used
# patterns can demand more.
$CanonicalPatterns = @(
    @{ Pattern = 'Error\.UnprocessableContent\.ForField\(';                              MinHits = 2; Description = 'Field-level validation factory' },
    @{ Pattern = 'Error\.UnprocessableContent\.ForRule\(';                               MinHits = 1; Description = 'Rule-level validation factory' },
    @{ Pattern = 'new\s+Error\.NotFound\(';                                              MinHits = 2; Description = 'Closed-ADT NotFound construction' },
    @{ Pattern = 'new\s+Error\.Conflict\(';                                              MinHits = 1; Description = 'Closed-ADT Conflict construction' },
    @{ Pattern = 'new\s+Error\.Forbidden\(';                                             MinHits = 1; Description = 'Closed-ADT Forbidden construction' },
    @{ Pattern = 'new\s+ResourceRef\(';                                                  MinHits = 1; Description = 'ResourceRef construction for NotFound payloads' },
    @{ Pattern = 'RetryAfterValue\.From(Seconds|Date)\(';                                MinHits = 1; Description = 'RetryAfterValue static factories' },
    @{ Pattern = '\bEvaluatePreconditions\b';                                            MinHits = 1; Description = 'Precondition evaluation API' },
    @{ Pattern = '\.AsActionResultAsync<';                                               MinHits = 1; Description = 'MVC bridge from ToHttpResponseAsync' },
    @{ Pattern = '\bIActorProvider\b';                                                   MinHits = 2; Description = 'Actor provider abstraction' },
    @{ Pattern = '\bIAuthorizeResource<';                                                MinHits = 1; Description = 'Resource-based authorization marker' },
    @{ Pattern = '\bIIdentifyResource<';                                                 MinHits = 1; Description = 'Resource identifier abstraction' },
    @{ Pattern = '\bSaveChangesResultAsync\b';                                           MinHits = 1; Description = 'Result-returning EF SaveChanges' },
    @{ Pattern = '\bApplyTrellisConventions\b';                                          MinHits = 1; Description = 'EF Core convention bundle' },
    @{ Pattern = '\bAddTrellisInterceptors\b';                                           MinHits = 1; Description = 'EF Core natural-VO LINQ interceptors' },
    @{ Pattern = 'CreatedAtRoute\(';                                                     MinHits = 1; Description = 'AOT-safe Created response (with named route)' },
    @{ Pattern = 'Result<Unit>';                                                         MinHits = 5; Description = 'Canonical command response type' },
    @{ Pattern = 'AddCachingActorProvider<';                                             MinHits = 1; Description = 'Per-request actor caching decorator' }
)

# Probe 3: surface-coverage exclusions.
# Public types whose simple name we would NOT expect to find in user-facing
# docs (internal-feeling helpers, generator artifacts, attribute markers,
# private-implementation interfaces, test infrastructure).
$SurfaceCoverageExcludedNames = @(
    # Generator-emitted / attribute-only types
    'GeneratedCodeAttribute', 'CompilerGeneratedAttribute',
    # AssemblyInfo / build-time artifacts
    'AssemblyAttributes', 'ThisAssembly',
    # Source-generated test scaffolding
    'StringSyntaxAttribute',
    # Names too generic to grep meaningfully
    'IExtension', 'IBuilder', 'IConfig', 'IOptions', 'IAdapter',
    'Builder', 'Options', 'Configuration', 'Extensions',
    'Helpers', 'Constants', 'Defaults'
)

# Probe 4: required frontmatter keys per collection.
$RequiredFrontmatterKeys = @{
    'articles'      = @('title', 'package', 'audience', 'last_verified')
    'api_reference' = @('package', 'audience', 'last_verified')
}

$FrontmatterExcludedFiles = @(
    'completeness-report.md',  # auto-generated
    'toc.yml'                  # not markdown
)

# ----------------------------------------------------------------------------
# Result accumulator
# ----------------------------------------------------------------------------

$script:Findings = @()

function Add-Finding {
    param(
        [Parameter(Mandatory)] [string] $Probe,
        [Parameter(Mandatory)] [ValidateSet('FAIL','WARN','INFO')] [string] $Severity,
        [Parameter(Mandatory)] [string] $Message,
        [string] $File,
        [int]    $Line
    )
    $script:Findings += [pscustomobject]@{
        Probe    = $Probe
        Severity = $Severity
        Message  = $Message
        File     = $File
        Line     = $Line
    }
}

# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------

function Get-DocFiles {
    param([string] $Directory)
    if (-not (Test-Path $Directory)) { return @() }
    Get-ChildItem -Path $Directory -Filter '*.md' -File |
        Where-Object { $FrontmatterExcludedFiles -notcontains $_.Name }
}

function Read-Frontmatter {
    param([string] $Path)
    $lines = Get-Content -Path $Path -Encoding UTF8
    if ($lines.Count -lt 3) { return $null }
    # Accept BOM-prefixed first line.
    $first = $lines[0] -replace '^\uFEFF', ''
    if ($first -ne '---') { return $null }
    $end = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -eq '---') { $end = $i; break }
    }
    if ($end -lt 0) { return $null }
    $keys = @{}
    for ($i = 1; $i -lt $end; $i++) {
        if ($lines[$i] -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:') {
            $keys[$Matches[1]] = $lines[$i].Substring($Matches[1].Length + 1).Trim().TrimStart(':').Trim()
        }
    }
    return $keys
}

# ----------------------------------------------------------------------------
# Probe 1: anti-pattern absence
# ----------------------------------------------------------------------------

function Invoke-Probe1 {
    Write-Host ""
    Write-Host "=== Probe 1: anti-pattern absence ===" -ForegroundColor Cyan
    $docFiles = @()
    $docFiles += Get-DocFiles -Directory $ArticlesDir
    $docFiles += Get-DocFiles -Directory $ApiReferenceDir

    $hits = 0
    foreach ($file in $docFiles) {
        $relPath = $file.FullName.Substring($RepositoryRoot.Length).TrimStart('\','/').Replace('\','/')
        if ($Probe1FileAllowlist -contains $relPath) { continue }
        $lineNo = 0
        foreach ($line in (Get-Content -Path $file.FullName -Encoding UTF8)) {
            $lineNo++
            if ($line -match 'v1-stale-ok|stale-doc-ok') { continue }
            foreach ($ap in $AntiPatterns) {
                if ($line -match $ap.Pattern) {
                    Add-Finding -Probe 'P1.AntiPattern' -Severity 'FAIL' `
                        -Message ("{0}: {1}" -f $ap.Reason, $line.Trim()) `
                        -File $relPath -Line $lineNo
                    $hits++
                }
            }
        }
    }
    if ($hits -eq 0) {
        Write-Host "  PASS — no anti-patterns found in $($docFiles.Count) docs"
    } else {
        Write-Host "  FAIL — $hits anti-pattern hits across $($docFiles.Count) docs" -ForegroundColor Red
    }
}

# ----------------------------------------------------------------------------
# Probe 2: canonical-form presence
# ----------------------------------------------------------------------------

function Invoke-Probe2 {
    Write-Host ""
    Write-Host "=== Probe 2: canonical-form presence ===" -ForegroundColor Cyan
    $articleFiles = Get-DocFiles -Directory $ArticlesDir
    $allText = ($articleFiles | ForEach-Object { Get-Content -Path $_.FullName -Encoding UTF8 -Raw }) -join "`n"

    $missing = 0
    foreach ($cp in $CanonicalPatterns) {
        $matches = [regex]::Matches($allText, $cp.Pattern)
        $count = $matches.Count
        if ($count -lt $cp.MinHits) {
            Add-Finding -Probe 'P2.Canonical' -Severity 'FAIL' `
                -Message ("'{0}' ({1}): found {2} occurrence(s), need {3}" -f $cp.Pattern, $cp.Description, $count, $cp.MinHits)
            $missing++
        }
    }
    if ($missing -eq 0) {
        Write-Host "  PASS — all $($CanonicalPatterns.Count) canonical patterns present at expected frequency"
    } else {
        Write-Host "  FAIL — $missing of $($CanonicalPatterns.Count) canonical patterns under-represented" -ForegroundColor Red
    }
}

# ----------------------------------------------------------------------------
# Probe 3: public-surface coverage
# ----------------------------------------------------------------------------

function Get-PublicTypeNames {
    $packageDirs = Get-ChildItem -Path $RepositoryRoot -Directory -Filter 'Trellis.*' |
        Where-Object { Test-Path (Join-Path $_.FullName 'src') }

    $typeRegex = [regex] ('(?m)^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*' +
                          '(?:class|record|interface|struct|enum|delegate)\s+' +
                          '(?:[A-Za-z_]\w*\s+)?([A-Za-z_]\w*)')

    $names = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($pkg in $packageDirs) {
        $srcDir = Join-Path $pkg.FullName 'src'
        if (-not (Test-Path $srcDir)) { continue }
        Get-ChildItem -Path $srcDir -Filter '*.cs' -File -Recurse |
            ForEach-Object {
                $content = Get-Content -Path $_.FullName -Raw
                foreach ($m in $typeRegex.Matches($content)) {
                    $name = $m.Groups[1].Value
                    if ($name -and -not ($SurfaceCoverageExcludedNames -contains $name)) {
                        [void] $names.Add($name)
                    }
                }
            }
    }
    return $names
}

function Invoke-Probe3 {
    Write-Host ""
    Write-Host "=== Probe 3: public-surface coverage ===" -ForegroundColor Cyan
    $publicTypes = Get-PublicTypeNames
    if ($publicTypes.Count -eq 0) {
        Add-Finding -Probe 'P3.SurfaceCoverage' -Severity 'WARN' `
            -Message 'No public types discovered. Probe configuration may be wrong.'
        Write-Host "  WARN — no public types discovered" -ForegroundColor Yellow
        return
    }

    $docFiles = @()
    $docFiles += Get-DocFiles -Directory $ArticlesDir
    $docFiles += Get-DocFiles -Directory $ApiReferenceDir
    $docText = ($docFiles | ForEach-Object { Get-Content -Path $_.FullName -Encoding UTF8 -Raw }) -join "`n"

    $covered = 0
    $uncovered = @()
    foreach ($name in ($publicTypes | Sort-Object)) {
        # Word-boundary match using the simple type name.
        if ($docText -match ('\b{0}\b' -f [regex]::Escape($name))) {
            $covered++
        } else {
            $uncovered += $name
        }
    }
    $total = $publicTypes.Count
    $pct = [math]::Round(100.0 * $covered / $total, 1)
    Write-Host ("  Coverage: {0}/{1} ({2}%) — threshold {3}%" -f $covered, $total, $pct, $SurfaceCoverageThreshold)

    if ($pct -lt $SurfaceCoverageThreshold) {
        Add-Finding -Probe 'P3.SurfaceCoverage' -Severity 'FAIL' `
            -Message ("Coverage {0}% below threshold {1}%. {2} types uncovered." -f $pct, $SurfaceCoverageThreshold, $uncovered.Count)
        Write-Host "  FAIL" -ForegroundColor Red
    } else {
        Write-Host "  PASS"
    }

    # Always emit per-type WARN findings for the uncovered set so reviewers
    # can see what's missing without having to re-run with verbose output.
    foreach ($name in $uncovered) {
        Add-Finding -Probe 'P3.SurfaceCoverage' -Severity 'WARN' `
            -Message ("Public type not mentioned in any doc: {0}" -f $name)
    }
}

# ----------------------------------------------------------------------------
# Probe 4: frontmatter completeness
# ----------------------------------------------------------------------------

function Invoke-Probe4 {
    Write-Host ""
    Write-Host "=== Probe 4: frontmatter completeness ===" -ForegroundColor Cyan
    $bad = 0
    $checked = 0
    foreach ($collection in $RequiredFrontmatterKeys.Keys) {
        $dir = Join-Path $RepositoryRoot ("docs/docfx_project/{0}" -f $collection)
        $required = $RequiredFrontmatterKeys[$collection]
        foreach ($file in (Get-DocFiles -Directory $dir)) {
            $checked++
            $relPath = $file.FullName.Substring($RepositoryRoot.Length).TrimStart('\','/').Replace('\','/')
            $fm = Read-Frontmatter -Path $file.FullName
            if ($null -eq $fm) {
                Add-Finding -Probe 'P4.Frontmatter' -Severity 'FAIL' `
                    -Message 'Missing or malformed YAML frontmatter (no `--- ... ---` block)' `
                    -File $relPath
                $bad++
                continue
            }
            $missing = @($required | Where-Object { -not $fm.ContainsKey($_) })
            if ($missing.Count -gt 0) {
                Add-Finding -Probe 'P4.Frontmatter' -Severity 'FAIL' `
                    -Message ("Missing required key(s): {0}" -f ($missing -join ', ')) `
                    -File $relPath
                $bad++
                continue
            }
            if ($fm['last_verified'] -notmatch '^\d{4}-\d{2}-\d{2}$') {
                Add-Finding -Probe 'P4.Frontmatter' -Severity 'FAIL' `
                    -Message ("Invalid last_verified format (expected YYYY-MM-DD): {0}" -f $fm['last_verified']) `
                    -File $relPath
                $bad++
            }
        }
    }
    if ($bad -eq 0) {
        Write-Host "  PASS — all $checked docs have valid frontmatter"
    } else {
        Write-Host "  FAIL — $bad of $checked docs have frontmatter issues" -ForegroundColor Red
    }
}

# ----------------------------------------------------------------------------
# Run
# ----------------------------------------------------------------------------

Push-Location $RepositoryRoot
try {
    Invoke-Probe1
    Invoke-Probe2
    Invoke-Probe3
    Invoke-Probe4

    Write-Host ""
    Write-Host "=== Summary ===" -ForegroundColor Cyan
    $byProbe = $script:Findings | Group-Object Probe, Severity | Sort-Object Name
    foreach ($g in $byProbe) {
        $color = if ($g.Name -match 'FAIL') { 'Red' } elseif ($g.Name -match 'WARN') { 'Yellow' } else { 'Gray' }
        Write-Host ("  {0}: {1}" -f $g.Name, $g.Count) -ForegroundColor $color
    }
    if ($script:Findings.Count -eq 0) {
        Write-Host "  (no findings)" -ForegroundColor Green
    }

    # Detailed findings (FAIL + optionally WARN).
    $detailed = $script:Findings | Where-Object {
        $_.Severity -eq 'FAIL' -or ($FailOnWarnings -and $_.Severity -eq 'WARN')
    }
    if ($detailed.Count -gt 0) {
        Write-Host ""
        Write-Host "=== Findings (detail) ===" -ForegroundColor Cyan
        foreach ($f in $detailed) {
            $loc = if ($f.File) { "  $($f.File)$(if ($f.Line) { ":$($f.Line)" })" } else { '' }
            Write-Host ("  [{0}] [{1}] {2}{3}" -f $f.Severity, $f.Probe, $f.Message, $loc)
        }
    }

    $hasFail = $script:Findings | Where-Object { $_.Severity -eq 'FAIL' }
    $hasWarn = $script:Findings | Where-Object { $_.Severity -eq 'WARN' }

    if ($hasFail.Count -gt 0)                            { exit 1 }
    if ($FailOnWarnings -and $hasWarn.Count -gt 0)       { exit 1 }
    exit 0
} finally {
    Pop-Location
}
