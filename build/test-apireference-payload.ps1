<#
.SYNOPSIS
    Packs first-party packages and verifies their experimental guidance payloads.
.DESCRIPTION
    Checks actual nupkg document bytes, the Core README's independently versioned
    tool instructions, tamper detection, and consumer restore/build isolation.
    CLI lifecycle coverage belongs to the independent AgentDocs repository.
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
        if ($project.BaseName -eq 'Trellis.Core') {
            $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
            try {
                $entry = $archive.GetEntry('NUGET_README.md')
                if (-not $entry) { throw 'Core NuGet README is missing' }
                $reader = [System.IO.StreamReader]::new($entry.Open())
                try { $readme = $reader.ReadToEnd() }
                finally { $reader.Dispose() }
            }
            finally { $archive.Dispose() }
            $command = 'dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.17 --tool-manifest .config/dotnet-tools.json'
            if ([regex]::Matches($readme, [regex]::Escape($command)).Count -ne 1 -or
                $readme -notmatch '(?m)^dotnet tool run agentdocs init <solution-or-project>\r?$' -or
                -not $readme.Contains('approvedPackages', [StringComparison]::Ordinal) -or
                -not $readme.Contains('dotnet tool run agentdocs sync', [StringComparison]::Ordinal) -or
                $readme.Contains('__TRELLIS_PACKAGE_VERSION__', [StringComparison]::Ordinal)) {
                throw 'Packed Core README must pin the independent AgentDocs tool and show its init, approval and sync steps.'
            }
            Write-Host "PASS $($package.Name) NuGet README pins independent Trellis.AgentDocs 0.1.0-preview.17"

            # The published validator is the authoring check for the contract and for discoverability. --strict makes a
            # warning (a link that leaves the package, a document the index does not reach, a malformed front matter
            # block, an oversized required set) fail the build, exactly as a contract error would.
            $toolDirectory = Join-Path $work 'agentdocs-tool'
            $install = & dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.17 --tool-path $toolDirectory 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Could not install Trellis.AgentDocs 0.1.0-preview.17 for validation:`n$($install | Out-String)" }
            $validation = & (Join-Path $toolDirectory 'agentdocs') validate $package.FullName --strict 2>&1
            if ($LASTEXITCODE -ne 0) { throw "agentdocs validate --strict rejected $($package.Name):`n$($validation | Out-String)" }
            Write-Host "PASS $($package.Name) agentdocs validate --strict: $($validation | Select-Object -Last 1)"
        }
    }

    $core = Get-ChildItem -Path $feed -Filter 'Trellis.Core.*.nupkg' | Select-Object -First 1
    if ($core) {
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
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Directory.Build.props'),
            '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>')
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Directory.Build.targets'), '<Project/>')
        [System.IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Trellis.Core" Version="$version" /></ItemGroup>
</Project>
"@)
        [System.IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), @"
<configuration><packageSources><clear/><add key="probe" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@)
        $env:NUGET_PACKAGES = Join-Path $work 'packages'
        $build = & dotnet build (Join-Path $consumer 'Consumer.csproj') -c $Configuration --nologo 2>&1
        $env:NUGET_PACKAGES = $originalPackages
        if ($LASTEXITCODE -ne 0) { throw "Consumer build failed: $($build | Out-String)" }
        if (@(Get-ChildItem -LiteralPath (Join-Path $consumer '.github') -Force).Count -ne 0 -or
            (Test-Path (Join-Path $consumer '.agentdocs')) -or
            (Test-Path (Join-Path $consumer 'AGENTS.md'))) {
            throw 'Normal consumer build wrote repository instructions or context'
        }
        Write-Host 'PASS consumer restore/build leaves instructions and .agentdocs untouched'
    }

    Write-Host "PASS: $($projects.Count) packed reference manifests and payloads"
}
finally {
    $env:NUGET_PACKAGES = $originalPackages
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
