<#
.SYNOPSIS
    Packs first-party packages and checks the experimental guidance payload in the actual nupkg.
.DESCRIPTION
    Uses a repository-local scratch feed. Reports each package, manifest, document hash, cohort,
    and legacy target check; fails on any missing, extra, or corrupted packaged asset.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string[]] $PackageIds = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PackageIds = @($PackageIds | ForEach-Object { $_.Split(',') })

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $root "artifacts\reference-payload-probe-$PID"
$feed = Join-Path $work 'feed'
$originalPackages = $env:NUGET_PACKAGES
$originalCliHome = $env:DOTNET_CLI_HOME

function Invoke-ScratchCommand {
    param([string] $Directory, [string] $Stage, [string[]] $CommandArguments)

    Push-Location $Directory
    try {
        $output = & dotnet @CommandArguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            $exitCode = $LASTEXITCODE
            $details = if ($Stage -eq 'Packed CLI init') { & dotnet tool list --local 2>&1 | Out-String } else { '' }
            throw "$Stage failed (exit $exitCode):`n$($output | Out-String)`n$details"
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-GitHubUntouched {
    param([string] $Consumer, [byte[]] $Original)

    $entries = @(Get-ChildItem -LiteralPath (Join-Path $Consumer '.github') -Recurse -Force)
    if ($entries.Count -ne 1 -or $entries[0].Name -ne 'customer.md' -or
        -not [System.Linq.Enumerable]::SequenceEqual(
            [byte[]]$Original, [byte[]][System.IO.File]::ReadAllBytes($entries[0].FullName))) {
        throw 'Packed CLI changed an existing .github file or added a .github entry'
    }
}

function Get-ManagedSnapshot {
    param([string] $Consumer)

    $files = @((Join-Path $Consumer 'AGENTS.md')) +
        @(Get-ChildItem -LiteralPath (Join-Path $Consumer '.trellis') -File -Recurse |
            ForEach-Object { $_.FullName })
    return @($files | Sort-Object | ForEach-Object {
        "$([System.IO.Path]::GetRelativePath($Consumer, $_))=$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash)"
    })
}

try {
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    $projects = @(Get-ChildItem -Path $root -Directory -Filter 'Trellis.*' |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'src') -Filter '*.csproj' -File -ErrorAction SilentlyContinue } |
        Where-Object { ([xml](Get-Content -LiteralPath $_.FullName -Raw)).SelectSingleNode('//IsPackable')?.InnerText -ne 'false' } |
        Where-Object { $PackageIds.Count -eq 0 -or $PackageIds -contains $_.BaseName })
    if ($projects.Count -eq 0) { throw "No packable projects selected: $($PackageIds -join ', ')" }

    foreach ($project in $projects) {
        $output = & dotnet pack $project.FullName -c $Configuration -o $feed --nologo 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Pack failed: $($project.Name)`n$($output | Out-String)" }
    }

    foreach ($project in $projects) {
        $package = Get-ChildItem -Path $feed -Filter "$($project.BaseName).*nupkg" |
            Where-Object { $_.Name -notlike '*.symbols.nupkg' } | Select-Object -First 1
        if (-not $package) { throw "Missing packed package: $($project.BaseName)" }
        & (Join-Path $PSScriptRoot 'validate-reference-manifest.ps1') -Package $package.FullName -Project $project.FullName -RepositoryRoot $root
        if ($LASTEXITCODE -ne 0) { throw "Manifest validation failed: $($package.Name)" }
    }

    $core = Get-ChildItem -Path $feed -Filter 'Trellis.Core.*.nupkg' | Select-Object -First 1
    if ($core -and ($PackageIds.Count -eq 0 -or $PackageIds -contains 'Trellis.Core')) {
        $corrupt = Join-Path $work 'corrupt.nupkg'
        Copy-Item -LiteralPath $core.FullName -Destination $corrupt -Force
        $zip = [System.IO.Compression.ZipFile]::Open($corrupt, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $zip.GetEntry('trellis/trellis-api-cookbook.md')
            if (-not $entry) { throw 'Core cookbook missing from packed payload' }
            $entry.Delete()
            $replacement = $zip.CreateEntry('trellis/trellis-api-cookbook.md')
            $writer = [System.IO.StreamWriter]::new($replacement.Open())
            try { $writer.Write('corrupted document') } finally { $writer.Dispose() }
        }
        finally { $zip.Dispose() }
        $validation = & (Join-Path $PSScriptRoot 'validate-reference-manifest.ps1') -Package $corrupt `
            -Project (Join-Path $root 'Trellis.Core\src\Trellis.Core.csproj') -RepositoryRoot $root -Quiet 2>&1
        if ($LASTEXITCODE -eq 0 -or ($validation | Out-String) -notmatch
            'sha256 mismatch for trellis/trellis-api-cookbook\.md') {
            throw "Corrupt packed bytes were not rejected for the expected hash mismatch: $($validation | Out-String)"
        }
        Write-Host 'PASS Core tampered packed document rejected (sha256 mismatch)'

        $consumer = Join-Path $work 'consumer'
        New-Item -ItemType Directory -Path (Join-Path $consumer '.github') -Force | Out-Null
        $version = $core.BaseName.Substring('Trellis.Core.'.Length)
        $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Trellis.Core" Version="$version" /></ItemGroup>
</Project>
"@
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Directory.Build.props'),
            '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>')
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Directory.Build.targets'), '<Project/>')
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $projectXml)
        [System.IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), @"
<configuration><packageSources><clear/><add key="probe" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@)
        $env:NUGET_PACKAGES = Join-Path $work 'packages'
        $build = & dotnet build (Join-Path $consumer 'Consumer.csproj') -c $Configuration --nologo 2>&1
        $env:NUGET_PACKAGES = $originalPackages
        if ($LASTEXITCODE -ne 0) { throw "Consumer build failed: $($build | Out-String)" }
        if (@(Get-ChildItem -LiteralPath (Join-Path $consumer '.github') -Force).Count -ne 0 -or
            (Test-Path (Join-Path $consumer '.trellis'))) {
            throw 'Normal consumer build wrote repository instructions or context'
        }
        Write-Host 'PASS consumer restore/build leaves .github and .trellis untouched'

        $readerProbe = Join-Path $work 'reader-probe'
        New-Item -ItemType Directory -Path $readerProbe -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $readerProbe 'Directory.Build.props'), '<Project/>')
        [System.IO.File]::WriteAllText((Join-Path $readerProbe 'Directory.Build.targets'), '<Project/>')
        $readerProject = Join-Path $root 'Trellis.Guidance.Reader\src\Trellis.Guidance.Reader.csproj'
        [System.IO.File]::WriteAllText((Join-Path $readerProbe 'ReaderProbe.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="$readerProject" /></ItemGroup>
</Project>
"@)
        [System.IO.File]::WriteAllText((Join-Path $readerProbe 'Program.cs'), @'
using System;
using System.Linq;
using Trellis.Guidance.Reader;
var result = GuidanceReader.Discover([args[0]]);
var core = result.Packages.Single(p => p.PackageId == "Trellis.Core");
if (!result.IsSuccessful || core.Status != GuidanceStatus.Valid ||
    core.Contribution?.Documents.All(d => d.Identity.PackagePath != "trellis/trellis-api-cookbook.md") != false ||
    core.Contribution.Documents.All(d => d.Identity.PackagePath != "trellis/trellis-start-here.md") ||
    core.Contribution.EntryPoints.Count != 1 ||
    core.Contribution.EntryPoints[0] != "trellis/trellis-start-here.md" ||
    !core.Contribution.PublisherMetadata.ContainsKey("org.trellis"))
    throw new Exception($"Packed Core manifest rejected: {core.Status}: {core.Diagnostic}");
Console.WriteLine("PASS shared reader validates restored packed Core manifest");
'@)
        $read = & dotnet run --project (Join-Path $readerProbe 'ReaderProbe.csproj') -c $Configuration -- `
            (Join-Path $consumer 'obj\project.assets.json') 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Shared reader probe failed: $($read | Out-String)" }
        Write-Host ($read | Out-String)

        $packedTool = Get-ChildItem -Path $feed -Filter 'Trellis.AgentContext.*.nupkg' | Select-Object -First 1
        if ($packedTool -and ($PackageIds.Count -eq 0 -or $PackageIds -contains 'Trellis.AgentContext')) {
            $gitOutput = & git -C $consumer init --quiet 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Scratch Git init failed: $($gitOutput | Out-String)" }
            $github = Join-Path $consumer '.github\customer.md'
            [System.IO.File]::WriteAllText($github, "# Customer-owned GitHub instructions`r`n",
                [System.Text.UTF8Encoding]::new($true))
            $githubBytes = [System.IO.File]::ReadAllBytes($github)
            $agents = Join-Path $consumer 'AGENTS.md'
            [System.IO.File]::WriteAllText($agents, "# Customer instructions`r`n",
                [System.Text.UTF8Encoding]::new($true))
            $agentBytes = [System.IO.File]::ReadAllBytes($agents)

            $env:DOTNET_CLI_HOME = Join-Path $work 'cli-home'
            $env:NUGET_PACKAGES = Join-Path $work 'packages'
            $toolManifestPath = Join-Path $consumer '.config\dotnet-tools.json'
            Invoke-ScratchCommand -Directory $consumer -Stage 'Create pinned tool manifest' `
                -CommandArguments @('new', 'tool-manifest', '--output', (Join-Path $consumer '.config'))
            $toolVersion = $packedTool.BaseName.Substring('Trellis.AgentContext.'.Length)
            Invoke-ScratchCommand -Directory $consumer -Stage 'Install packed local CLI' `
                -CommandArguments @('tool', 'install', 'Trellis.AgentContext', '--version', $toolVersion,
                    '--add-source', $feed, '--tool-manifest', $toolManifestPath)
            $toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw |
                ConvertFrom-Json
            if ($toolManifest.isRoot -ne $true -or
                $toolManifest.tools.'trellis.agentcontext'.version -ne $toolVersion -or
                $toolManifest.tools.'trellis.agentcontext'.commands -notcontains 'trellis') {
                throw "Local tool manifest does not pin trellis $toolVersion with isRoot=true"
            }
            Invoke-ScratchCommand -Directory $consumer -Stage 'Restore pinned local CLI' `
                -CommandArguments @('tool', 'restore', '--tool-manifest', $toolManifestPath, '--add-source', $feed)

            Invoke-ScratchCommand -Directory $consumer -Stage 'Packed CLI init' `
                -CommandArguments @('tool', 'run', 'trellis', 'agent', 'init', 'Consumer.csproj')
            $context = Join-Path $consumer '.trellis'
            $required = @('README.md', 'agent-context.json',
                'api-reference\trellis-start-here.md', 'api-reference\trellis-api-cookbook.md',
                'api-reference\trellis-api-core.md')
            foreach ($relative in $required) {
                if (-not (Test-Path -LiteralPath (Join-Path $context $relative) -PathType Leaf)) {
                    throw "Packed CLI init omitted .trellis\$relative"
                }
            }
            if ((Get-Content -LiteralPath (Join-Path $context 'README.md') -Raw) -notmatch
                'api-reference/trellis-start-here\.md' -or
                (Get-Content -LiteralPath $agents -Raw) -notmatch '<!-- trellis-agent-context:start -->') {
                throw 'Packed CLI init did not route the index and AGENTS.md to the Core router'
            }
            $state = Get-Content -LiteralPath (Join-Path $context 'agent-context.json') -Raw | ConvertFrom-Json
            if ($state.EntryPoints -notcontains 'api-reference/trellis-start-here.md' -or
                @($state.References).Count -lt 3) {
                throw 'Packed CLI manifest omitted its router entry point or reference provenance'
            }
            Assert-GitHubUntouched -Consumer $consumer -Original $githubBytes
            Write-Host "PASS packed trellis $toolVersion init installs router, docs, index and owned instructions"

            $snapshot = @(Get-ManagedSnapshot -Consumer $consumer)
            Invoke-ScratchCommand -Directory $consumer -Stage 'Packed CLI check' `
                -CommandArguments @('tool', 'run', 'trellis', 'agent', 'check')
            if (@(Compare-Object $snapshot @(Get-ManagedSnapshot -Consumer $consumer)).Count -ne 0) {
                throw 'Packed CLI check changed managed files or instructions'
            }
            Assert-GitHubUntouched -Consumer $consumer -Original $githubBytes
            Write-Host 'PASS packed CLI check is read-only'

            Invoke-ScratchCommand -Directory $consumer -Stage 'Packed CLI sync' `
                -CommandArguments @('tool', 'run', 'trellis', 'agent', 'sync')
            if (@(Compare-Object $snapshot @(Get-ManagedSnapshot -Consumer $consumer)).Count -ne 0) {
                throw 'Packed CLI sync changed an unchanged context'
            }
            Assert-GitHubUntouched -Consumer $consumer -Original $githubBytes
            Write-Host 'PASS packed CLI sync preserves an unchanged context'

            Invoke-ScratchCommand -Directory $consumer -Stage 'Packed CLI remove' `
                -CommandArguments @('tool', 'run', 'trellis', 'agent', 'remove')
            if (-not [System.Linq.Enumerable]::SequenceEqual(
                [byte[]]$agentBytes, [byte[]][System.IO.File]::ReadAllBytes($agents)) -or
                ((Test-Path $context) -and @(Get-ChildItem -LiteralPath $context -File -Recurse).Count -gt 0)) {
                throw 'Packed CLI remove did not restore instructions and remove owned context files'
            }
            Assert-GitHubUntouched -Consumer $consumer -Original $githubBytes
            Write-Host 'PASS packed CLI remove restores original instructions; .github remained untouched'
            $env:NUGET_PACKAGES = $originalPackages
            $env:DOTNET_CLI_HOME = $originalCliHome
        }
    }

    Write-Host "PASS: $($projects.Count) packed reference manifests and payloads"
}
finally {
    $env:NUGET_PACKAGES = $originalPackages
    $env:DOTNET_CLI_HOME = $originalCliHome
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
