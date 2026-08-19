#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Probes the API-reference doc delivery mechanism end to end: pack a real package, restore it
    into a scratch consumer outside this repository, build, and assert what lands in .github/.

.DESCRIPTION
    build/Trellis.ApiReference.targets decides whether any Trellis documentation ever reaches a
    consumer's LLM. It is an 11-level unrolled directory walk with a .git boundary, and unit tests
    cannot cover it because the behaviour only exists once the package is packed, restored and
    imported by NuGet. This probe is the only thing that exercises that path.

    Scenarios:
      1. Nearest .github wins            - a .github closer than the repo root is preferred.
      2. Bounded by the .git root        - a .github ABOVE the repo root is never written to.
      3. Fallback creates .github        - no .github inside the repo means one is created at root.
      4. TrellisApiReferenceRoot         - explicit override wins over the walk.
      5. TrellisDisableApiReferenceSync  - opts out entirely.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$work = Join-Path ([System.IO.Path]::GetTempPath()) "trellis-payload-probe-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
$feed = Join-Path $work 'feed'

$script:failures = @()
$script:originalNuGetPackages = $env:NUGET_PACKAGES

function Assert-Condition {
    param([string] $Scenario, [string] $Because, [bool] $Condition)

    if ($Condition) {
        Write-Host "  PASS  $Scenario :: $Because"
    }
    else {
        Write-Host "  FAIL  $Scenario :: $Because" -ForegroundColor Red
        $script:failures += "$Scenario :: $Because"
    }
}

# Every reference in the repository must reach a consumer that references Trellis.Core alone.
# Derived rather than hand-listed: a hand-listed set would silently stop covering a doc added
# later, which is the exact failure - a reference no consumer receives - this probe exists to catch.
$docManifest = Import-PowerShellDataFile -LiteralPath (Join-Path $repoRoot 'docs/api-reference-docs.psd1')
$expectedDocs = @(
    Get-ChildItem -Path (Join-Path $repoRoot 'docs/docfx_project/api_reference') -Filter '*.md' -File |
        Where-Object { $docManifest.UnshippedDocs -notcontains $_.Name } |
        ForEach-Object { $_.Name }
)

function New-ScratchConsumer {
    <#
        Builds a consumer project tree. The consumer deliberately lives OUTSIDE the Trellis repo so
        the walk cannot accidentally find this repository's own .github directory.
    #>
    param(
        [string] $Path,
        [string] $PackageVersion,
        [hashtable] $Properties = @{},
        [hashtable] $AdditionalPackages = @{},
        [switch] $OmitCore
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    $props = ($Properties.GetEnumerator() | ForEach-Object { "    <$($_.Key)>$($_.Value)</$($_.Key)>" }) -join "`n"
    $coreRef = if ($OmitCore) { '' } else { "    <PackageReference Include=`"Trellis.Core`" Version=`"$PackageVersion`" />" }
    $extraRefs = ($AdditionalPackages.GetEnumerator() | ForEach-Object {
        "    <PackageReference Include=`"$($_.Key)`" Version=`"$($_.Value)`" />"
    }) -join "`n"

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
$props
  </PropertyGroup>
  <ItemGroup>
$coreRef
$extraRefs
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $Path 'Consumer.csproj') -Encoding utf8

    'public static class Probe { public static int Value => 1; }' |
        Set-Content -Path (Join-Path $Path 'Probe.cs') -Encoding utf8
}

function New-SatellitePackage {
    <#
        Builds a stand-in for a Trellis package published from ANOTHER repository - the shape
        Trellis.ServiceLevelIndicators and Trellis.ResourceNaming.Azure have. It ships its own
        reference plus build/Trellis.ApiReference.Payload.targets, and deliberately does NOT
        ship the copy logic: proving the doc still lands is what shows a satellite can rely on
        Trellis.Core for that logic instead of duplicating a file that would then drift.
    #>
    param([string] $Path, [string] $CoreVersion, [string] $DocName)

    New-Item -ItemType Directory -Path (Join-Path $Path 'trellis') -Force | Out-Null

    "# Satellite reference`n`nProbe payload for $DocName." |
        Set-Content -Path (Join-Path $Path "trellis/$DocName") -Encoding utf8

    Copy-Item -Path (Join-Path $repoRoot 'build/Trellis.ApiReference.Payload.targets') `
              -Destination (Join-Path $Path 'Payload.targets')

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Trellis.SatelliteProbe</PackageId>
    <Version>1.0.0-probe</Version>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>`$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Trellis.Core" Version="$CoreVersion" />
  </ItemGroup>
  <ItemGroup>
    <None Include="trellis/$DocName" Pack="true" PackagePath="trellis\" />
    <None Include="Payload.targets" Pack="true" PackagePath="build\Trellis.SatelliteProbe.targets" />
    <None Include="Payload.targets" Pack="true" PackagePath="buildTransitive\Trellis.SatelliteProbe.targets" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $Path 'Satellite.csproj') -Encoding utf8
}

function New-NuGetConfig {
    param([string] $Path)

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="probe-feed" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $Path 'nuget.config') -Encoding utf8
}

function Invoke-ConsumerBuild {
    param([string] $ProjectDir)

    Push-Location $ProjectDir
    try {
        $output = dotnet build -c $Configuration --nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ($output | Out-String)
            throw "Consumer build failed in $ProjectDir"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-DocNames {
    param([string] $GitHubDir)

    # The comma operator keeps an empty result an empty ARRAY; a bare `return @()` unrolls to
    # $null on the pipeline, which then blows up on .Count under Set-StrictMode.
    if (-not (Test-Path $GitHubDir)) { return , @() }
    return , @(Get-ChildItem -Path $GitHubDir -Filter '*.md' -File | ForEach-Object { $_.Name })
}

try {
    Write-Host "Probe workspace: $work"
    New-Item -ItemType Directory -Path $feed -Force | Out-Null

    Write-Host "`nPacking Trellis.Core into the probe feed..."

    # NuGet extracts packages into the global packages folder keyed by id+version, so re-packing
    # the SAME version leaves restore using the previously extracted copy - the probe would then
    # silently validate a stale package and stop biting. nbgv owns the version here and overrides
    # -p:PackageVersion, so pin an isolated cache per run instead. This also keeps throwaway probe
    # packages out of the developer's real global cache.
    $env:NUGET_PACKAGES = Join-Path $work 'nuget-cache'
    New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null

    $packOutput = dotnet pack (Join-Path $repoRoot 'Trellis.Core/src/Trellis.Core.csproj') `
        -c $Configuration -o $feed --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ($packOutput | Out-String)
        throw 'Pack failed.'
    }

    $package = Get-ChildItem -Path $feed -Filter 'Trellis.Core.*.nupkg' | Select-Object -First 1
    if (-not $package) { throw "No Trellis.Core package produced in $feed" }

    # Trellis.Core.3.0.0-alpha.449.gabc1234.nupkg -> 3.0.0-alpha.449.gabc1234
    $version = $package.BaseName -replace '^Trellis\.Core\.', ''
    Write-Host "Packed version: $version"

    # ---------------------------------------------------------------- Scenario 1: nearest .github
    # Repo root has a .github, but a nearer one sits between the project and the root. The nearer
    # one must win, otherwise a monorepo's per-service instructions would be bypassed.
    $s1Root = Join-Path $work 's1-nearest'
    $s1Project = Join-Path $s1Root 'src/services/orders'
    New-Item -ItemType Directory -Path (Join-Path $s1Root '.git') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $s1Root '.github') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $s1Root 'src/services/.github') -Force | Out-Null
    New-ScratchConsumer -Path $s1Project -PackageVersion $version
    New-NuGetConfig -Path $s1Root
    Invoke-ConsumerBuild -ProjectDir $s1Project

    $s1Near = Get-DocNames (Join-Path $s1Root 'src/services/.github')
    $s1Far = Get-DocNames (Join-Path $s1Root '.github')

    Write-Host "`nScenario 1 - nearest .github wins"
    foreach ($doc in $expectedDocs) {
        Assert-Condition -Scenario 'nearest' -Because "$doc delivered to the nearest .github" -Condition ($s1Near -contains $doc)
    }
    Assert-Condition -Scenario 'nearest' -Because 'the repo-root .github was left untouched' -Condition ($s1Far.Count -eq 0)

    # ------------------------------------------------------- Scenario 2: never escape the .git root
    # A .github exists ABOVE the consumer's repository. Writing there would leak files into an
    # unrelated parent checkout, so the walk must stop at .git and create one at the repo root.
    $s2Outer = Join-Path $work 's2-outer'
    $s2Root = Join-Path $s2Outer 'inner-repo'
    New-Item -ItemType Directory -Path (Join-Path $s2Outer '.github') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $s2Root '.git') -Force | Out-Null
    $s2Project = Join-Path $s2Root 'src/app'
    New-ScratchConsumer -Path $s2Project -PackageVersion $version
    New-NuGetConfig -Path $s2Root
    Invoke-ConsumerBuild -ProjectDir $s2Project

    $s2Outside = Get-DocNames (Join-Path $s2Outer '.github')
    $s2Inside = Get-DocNames (Join-Path $s2Root '.github')

    Write-Host "`nScenario 2 - bounded by the .git root"
    Assert-Condition -Scenario 'bounded' -Because 'no docs escaped above the .git root' -Condition ($s2Outside.Count -eq 0)
    Assert-Condition -Scenario 'bounded' -Because 'docs landed in a .github created at the repo root' -Condition ($s2Inside -contains 'trellis-start-here.md')

    # ------------------------------------------------------------- Scenario 3: explicit root override
    $s3Root = Join-Path $work 's3-override'
    $s3Target = Join-Path $s3Root 'custom-root'
    New-Item -ItemType Directory -Path (Join-Path $s3Root '.git') -Force | Out-Null
    New-Item -ItemType Directory -Path $s3Target -Force | Out-Null
    $s3Project = Join-Path $s3Root 'src/app'
    New-ScratchConsumer -Path $s3Project -PackageVersion $version -Properties @{ TrellisApiReferenceRoot = $s3Target }
    New-NuGetConfig -Path $s3Root
    Invoke-ConsumerBuild -ProjectDir $s3Project

    Write-Host "`nScenario 3 - TrellisApiReferenceRoot override"
    Assert-Condition -Scenario 'override' -Because 'docs landed under the overridden root' `
        -Condition ((Get-DocNames (Join-Path $s3Target '.github')) -contains 'trellis-start-here.md')
    Assert-Condition -Scenario 'override' -Because 'the repo root .github was not used' `
        -Condition ((Get-DocNames (Join-Path $s3Root '.github')).Count -eq 0)

    # ------------------------------------------------------------------ Scenario 4: opt out entirely
    $s4Root = Join-Path $work 's4-disabled'
    New-Item -ItemType Directory -Path (Join-Path $s4Root '.git') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $s4Root '.github') -Force | Out-Null
    $s4Project = Join-Path $s4Root 'src/app'
    New-ScratchConsumer -Path $s4Project -PackageVersion $version -Properties @{ TrellisDisableApiReferenceSync = 'true' }
    New-NuGetConfig -Path $s4Root
    Invoke-ConsumerBuild -ProjectDir $s4Project

    Write-Host "`nScenario 4 - TrellisDisableApiReferenceSync"
    Assert-Condition -Scenario 'disabled' -Because 'no docs were copied when sync is disabled' `
        -Condition ((Get-DocNames (Join-Path $s4Root '.github')).Count -eq 0)

    # ------------------------------------------------------- Scenario 5: satellite package payload
    # A package from another repository contributes its own reference while relying on
    # Trellis.Core for the copy logic. This is the federation contract: if it breaks, every
    # out-of-repo Trellis package has to carry its own copy of the directory walk and drift.
    $satelliteDoc = 'trellis-api-satelliteprobe.md'
    $satelliteSrc = Join-Path $work 'satellite-src'
    New-SatellitePackage -Path $satelliteSrc -CoreVersion $version -DocName $satelliteDoc
    New-NuGetConfig -Path $satelliteSrc

    $satellitePack = dotnet pack (Join-Path $satelliteSrc 'Satellite.csproj') -c $Configuration -o $feed --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ($satellitePack | Out-String)
        throw 'Satellite pack failed.'
    }

    $s5Root = Join-Path $work 's5-satellite'
    New-Item -ItemType Directory -Path (Join-Path $s5Root '.git') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $s5Root '.github') -Force | Out-Null
    $s5Project = Join-Path $s5Root 'src/app'

    # Reference ONLY the satellite. Trellis.Core arrives transitively, which is how a real
    # consumer of a satellite package acquires it - and it is the case that proves the copy
    # logic flows through buildTransitive rather than needing a direct reference. Referencing
    # Core directly here would pass even if buildTransitive were broken.
    New-ScratchConsumer -Path $s5Project -PackageVersion $version -OmitCore `
        -AdditionalPackages @{ 'Trellis.SatelliteProbe' = '1.0.0-probe' }
    New-NuGetConfig -Path $s5Root
    Invoke-ConsumerBuild -ProjectDir $s5Project

    $s5Docs = Get-DocNames (Join-Path $s5Root '.github')

    Write-Host "`nScenario 5 - satellite package contributes its own reference"
    Assert-Condition -Scenario 'satellite' -Because 'the satellite reference was delivered' `
        -Condition ($s5Docs -contains $satelliteDoc)
    Assert-Condition -Scenario 'satellite' -Because 'the first-party set arrived via a transitive Trellis.Core' `
        -Condition ($s5Docs -contains 'trellis-api-efcore-outbox.md')
}
finally {
    $env:NUGET_PACKAGES = $script:originalNuGetPackages

    if (Test-Path $work) {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host "API reference payload probe FAILED ($($script:failures.Count) assertion(s)):" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'API reference payload probe passed: docs reach the correct .github in every scenario.' -ForegroundColor Green
exit 0
