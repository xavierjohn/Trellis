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
    $metadataProbe = Join-Path $work 'metadata'
    New-Item -ItemType Directory -Path $metadataProbe -Force | Out-Null
    $adapter = [System.Security.SecurityElement]::Escape((Join-Path $root 'build\Trellis.Guidance.Metadata.targets'))
    $utf8Bom = [System.Text.UTF8Encoding]::new($true)
    $cases = @(
        @{ Name = 'quoted'; Text = "---`nagent_usage: required`nagent_description: `"Read before using Trellis.`"`n---`n# Guide"; Expected = 'trellis/guide.md|required|Read before using Trellis.' },
        @{ Name = 'plain'; Text = "---`nagent_usage: onDemand`nagent_description: Open when configuring Trellis.`n---`n# Guide"; Expected = 'trellis/guide.md|onDemand|Open when configuring Trellis.' },
        @{ Name = 'missing-front-matter'; Text = '# Guide'; Error = 'missing guidance front matter' },
        @{ Name = 'unclosed'; Text = "---`nagent_usage: required`nagent_description: Read before using Trellis."; Error = 'front matter must close' },
        @{ Name = 'body'; Text = "---`n# Body`nagent_usage: required`nagent_description: Read before using Trellis.`n---"; Error = 'front matter must close' },
        @{ Name = 'missing-usage'; Text = "---`nagent_description: Read before using Trellis.`n---"; Error = 'agent_usage must be' },
        @{ Name = 'invalid-usage'; Text = "---`nagent_usage: Required`nagent_description: Read before using Trellis.`n---"; Error = 'agent_usage must be' },
        @{ Name = 'missing-description'; Text = "---`nagent_usage: required`n---"; Error = 'agent_description is required' },
        @{ Name = 'blank-description'; Text = "---`nagent_usage: required`nagent_description: `"   `"`n---"; Error = 'agent_description is required' },
        @{ Name = 'single-quoted'; Text = "---`nagent_usage: required`nagent_description: 'Read before using Trellis.'`n---"; Error = 'double-quoted or plain scalar' },
        @{ Name = 'unclosed-quote'; Text = "---`nagent_usage: required`nagent_description: `"Read before using Trellis.`n---"; Error = 'double-quoted or plain scalar' },
        @{ Name = 'escaped'; Text = "---`nagent_usage: required`nagent_description: `"Read \n before using Trellis.`"`n---"; Error = 'unsupported backslash escape' }
    )
    foreach ($case in $cases) {
        $document = Join-Path $metadataProbe 'guide.md'
        $result = Join-Path $metadataProbe 'result.txt'
        $probe = Join-Path $metadataProbe 'Probe.proj'
        [System.IO.File]::WriteAllText($document, $case.Text, $utf8Bom)
        [System.IO.File]::WriteAllText($probe, @"
<Project>
  <Import Project="$adapter" />
  <ItemGroup><PackageGuidanceItem Include="guide.md" PackagePath="trellis/guide.md" /></ItemGroup>
  <Target Name="GeneratePackageGuidanceManifestFromItems" />
  <Target Name="Probe" DependsOnTargets="ReadTrellisGuidanceMetadata">
    <WriteLinesToFile File="result.txt" Lines="@(PackageGuidanceItem -> '%(PackagePath)|%(Usage)|%(Description)')" Overwrite="true" />
  </Target>
</Project>
"@, $utf8Bom)
        $output = & dotnet msbuild $probe -t:Probe -nologo -verbosity:quiet 2>&1
        $exitCode = $LASTEXITCODE
        if ($case.ContainsKey('Expected')) {
            if ($exitCode -ne 0 -or [System.IO.File]::ReadAllText($result).TrimEnd() -cne $case.Expected) {
                throw "Front-matter mapping failed for $($case.Name): $($output | Out-String)"
            }
        } elseif ($exitCode -eq 0 -or ($output | Out-String) -notmatch [regex]::Escape($case.Error)) {
            throw "Invalid front matter was not rejected for $($case.Name): $($output | Out-String)"
        }
    }
    [System.IO.File]::WriteAllText($document, $cases[0].Text, $utf8Bom)
    foreach ($name in @('z.md', 'a.md')) {
        [System.IO.File]::WriteAllText((Join-Path $metadataProbe $name), $cases[1].Text, $utf8Bom)
    }
    $projectText = [System.IO.File]::ReadAllText($probe).Replace(
        '<ItemGroup><PackageGuidanceItem Include="guide.md" PackagePath="trellis/guide.md" /></ItemGroup>',
        '<ItemGroup><PackageGuidanceItem Include="z.md" PackagePath="trellis/z.md" /><PackageGuidanceItem Include="guide.md" PackagePath="trellis/guide.md" /><PackageGuidanceItem Include="a.md" PackagePath="trellis/a.md" /></ItemGroup>')
    [System.IO.File]::WriteAllText($probe, $projectText, $utf8Bom)
    $output = & dotnet msbuild $probe -t:Probe -nologo -verbosity:quiet 2>&1
    if ($LASTEXITCODE -ne 0 -or ([System.IO.File]::ReadAllLines($result) -join "`n") -cne
        ($cases[0].Expected, 'trellis/a.md|onDemand|Open when configuring Trellis.',
            'trellis/z.md|onDemand|Open when configuring Trellis.' -join "`n")) {
        throw "Required-first and ordinal document ordering failed: $($output | Out-String)"
    }
    Write-Host 'PASS front-matter mapping, fail-closed metadata validation and document ordering'
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
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $reader = [System.IO.StreamReader]::new($archive.GetEntry("$($project.BaseName).nuspec").Open())
            try { [xml]$nuspec = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
            if ($nuspec.SelectSingleNode('//*[local-name()="dependency" and @id="Trellis.AgentDocs.Packaging"]')) {
                throw "$($package.Name) leaks the publisher-only packaging helper dependency"
            }
            if ($project.BaseName -eq 'Trellis.Core') {
                $reader = [System.IO.StreamReader]::new($archive.GetEntry('guidance/reference-manifest.json').Open())
                try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
                finally { $reader.Dispose() }
                if ($manifest.Count -ne 2 -or -not $manifest.ContainsKey('schemaVersion') -or
                    -not $manifest.ContainsKey('documents')) {
                    throw 'Core must publish the standard guidance manifest without unused cohort metadata'
                }
                if ($manifest.documents[0].path -cne 'trellis/trellis-start-here.md') {
                    throw 'Core must retain required-first document ordering'
                }
            }
        }
        finally { $archive.Dispose() }
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
            $command = 'dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-manifest .config/dotnet-tools.json'
            if ([regex]::Matches($readme, [regex]::Escape($command)).Count -ne 1 -or
                $readme -notmatch '(?m)^dotnet tool run agentdocs init <solution-or-project>\r?$' -or
                -not $readme.Contains('approvedPackages', [StringComparison]::Ordinal) -or
                -not $readme.Contains('dotnet tool run agentdocs sync', [StringComparison]::Ordinal) -or
                $readme.Contains('__TRELLIS_PACKAGE_VERSION__', [StringComparison]::Ordinal)) {
                throw 'Packed Core README must pin the independent AgentDocs tool and show its init, approval and sync steps.'
            }
            Write-Host "PASS $($package.Name) NuGet README pins independent Trellis.AgentDocs 0.1.0-preview.20"

            # The published validator is the authoring check for the contract and for discoverability. --strict makes a
            # warning (a link that leaves the package, a document the index does not reach, a malformed front matter
            # block, an oversized required set) fail the build, exactly as a contract error would.
            $toolDirectory = Join-Path $work 'agentdocs-tool'
            $install = & dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-path $toolDirectory 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Could not install Trellis.AgentDocs 0.1.0-preview.20 for validation:`n$($install | Out-String)" }
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
