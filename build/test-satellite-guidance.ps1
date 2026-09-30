<#
.SYNOPSIS
    Verifies an independently published Trellis satellite can use AgentDocs.Packaging.
.DESCRIPTION
    Checks the packed manifest/hash, private build-only helper, and read-only consumer
    restore/build. Generic publisher and CLI lifecycle probes live in the sibling repository.
#>
[CmdletBinding()]
param(
    [string] $WorkDirectory = (Join-Path (Join-Path $PSScriptRoot '..\artifacts') "satellite-probe-$PID"),
    [string] $HelperPackagePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $WorkDirectory 'satellite'
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
$unrelated = Join-Path $WorkDirectory 'unrelated'
$rootTargets = Join-Path $root 'Directory.Build.targets'
if (Test-Path -LiteralPath $WorkDirectory) { throw "Probe directory already exists: $WorkDirectory" }
$keepWorkDirectory = $PSBoundParameters.ContainsKey('WorkDirectory')
try {
New-Item -ItemType Directory -Path $source, $feed, $consumer, $unrelated -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $WorkDirectory 'Directory.Build.props'), '<Project/>')
[System.IO.File]::WriteAllText((Join-Path $WorkDirectory 'Directory.Build.targets'), '<Project/>')

[System.IO.File]::WriteAllText((Join-Path $unrelated 'guide.txt'), 'Unrelated package.')
[System.IO.File]::WriteAllText((Join-Path $unrelated 'Unrelated.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Unrelated.Probe</PackageId>
    <Version>1.0.0</Version>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup><None Include="guide.txt" Pack="true" PackagePath="guide.txt" /></ItemGroup>
  <Import Project="$rootTargets" />
</Project>
"@)
$dotnet = (Get-Command dotnet).Source
$previousPath = $env:PATH
try {
    $env:PATH = Split-Path -Parent $dotnet
    & $dotnet pack (Join-Path $unrelated 'Unrelated.csproj') -o $feed --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Unrelated pack should not require pwsh.' }
}
finally { $env:PATH = $previousPath }
$unrelatedArchive = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $feed 'Unrelated.Probe.1.0.0.nupkg'))
try {
    if ($unrelatedArchive.GetEntry('guidance/reference-manifest.json')) {
        throw 'Unrelated pack unexpectedly acquired a Trellis guidance manifest.'
    }
}
finally { $unrelatedArchive.Dispose() }
Write-Host 'PASS unrelated package packs with imported root target and no pwsh on PATH.'

$reference = Join-Path $source 'guides\reference.md'
New-Item -ItemType Directory -Path (Split-Path -Parent $reference) -Force | Out-Null
[System.IO.File]::WriteAllText($reference, "# Independent satellite`n", [System.Text.UTF8Encoding]::new($true))
if ($HelperPackagePath) {
    Copy-Item -LiteralPath $HelperPackagePath -Destination $feed
} else {
    $helperVersion = '0.1.0-preview.15'
    $url = "https://api.nuget.org/v3-flatcontainer/trellis.agentdocs.packaging/$helperVersion/trellis.agentdocs.packaging.$helperVersion.nupkg"
    try {
        Invoke-WebRequest -Uri $url -OutFile (Join-Path $feed "Trellis.AgentDocs.Packaging.$helperVersion.nupkg")
    }
    catch {
        throw "Trellis.AgentDocs.Packaging $helperVersion is not available on NuGet.org. Supply -HelperPackagePath for an explicit local package: $_"
    }
}
$helperPackage = Get-ChildItem -LiteralPath $feed -Filter 'Trellis.AgentDocs.Packaging.*.nupkg' |
    Select-Object -First 1
if (-not $helperPackage) { throw 'AgentDocs helper package is missing.' }
$helperVersion = $helperPackage.BaseName.Substring('Trellis.AgentDocs.Packaging.'.Length)
$helperArchive = [System.IO.Compression.ZipFile]::OpenRead($helperPackage.FullName)
try {
    if (-not $helperArchive.GetEntry('build/Trellis.AgentDocs.Packaging.targets') -or
        $helperArchive.GetEntry('buildTransitive/Trellis.AgentDocs.Packaging.targets') -or
        @($helperArchive.Entries | Where-Object { $_.FullName -like 'lib/*' }).Count -ne 0) {
        throw 'Shared helper must ship only a publisher-side build target, not runtime code.'
    }
}
finally { $helperArchive.Dispose() }
[System.IO.File]::WriteAllText((Join-Path $source 'Satellite.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Trellis.SatelliteProbe</PackageId>
    <Version>1.0.0</Version>
    <PackageGuidanceDocument>$reference</PackageGuidanceDocument>
    <PackageGuidancePath>trellis/trellis-api-satelliteprobe.md</PackageGuidancePath>
    <PackageGuidanceUsage>onDemand</PackageGuidanceUsage>
    <PackageGuidanceDescription>Open when working with the satellite probe.</PackageGuidanceDescription>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Trellis.AgentDocs.Packaging" Version="$helperVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@)
& dotnet restore (Join-Path $source 'Satellite.csproj') --source $feed `
    "-p:RestorePackagesPath=$(Join-Path $WorkDirectory 'packages')" --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Independent satellite could not restore the shared helper.' }
& dotnet pack (Join-Path $source 'Satellite.csproj') --no-restore -o $feed --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Independent satellite pack failed.' }
$package = Join-Path $feed 'Trellis.SatelliteProbe.1.0.0.nupkg'
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $manifest = $archive.GetEntry('guidance/reference-manifest.json')
    $document = $archive.GetEntry('trellis/trellis-api-satelliteprobe.md')
    if ($null -eq $manifest -or $null -eq $document) {
        throw 'Independent satellite must pack both its reference and its guidance manifest.'
    }
    if ($archive.Entries | Where-Object {
        $_.FullName -like 'build/*' -or $_.FullName -like 'buildTransitive/*'
    }) {
        throw 'Satellite shipped a publisher target to consumers.'
    }
    $nuspecReader = [System.IO.StreamReader]::new($archive.GetEntry('Trellis.SatelliteProbe.nuspec').Open())
    try {
        if ($nuspecReader.ReadToEnd().Contains('Trellis.AgentDocs.Packaging', [StringComparison]::Ordinal)) {
            throw 'Satellite leaked a dependency on the build-only helper.'
        }
    }
    finally { $nuspecReader.Dispose() }
    $manifestReader = [System.IO.StreamReader]::new($manifest.Open())
    try { $metadata = $manifestReader.ReadToEnd() | ConvertFrom-Json }
    finally { $manifestReader.Dispose() }
    $documentStream = [System.IO.MemoryStream]::new()
    try {
        $document.Open().CopyTo($documentStream)
        $digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($documentStream.ToArray()))
    }
    finally { $documentStream.Dispose() }
    if ($metadata.schemaVersion -ne 1 -or
        @($metadata.documents).Count -ne 1 -or
        $metadata.documents[0].path -ne 'trellis/trellis-api-satelliteprobe.md' -or
        $metadata.documents[0].sha256 -ne $digest.ToLowerInvariant() -or
        $metadata.documents[0].usage -ne 'onDemand' -or
        $metadata.documents[0].description -ne 'Open when working with the satellite probe.' -or
        $null -ne $metadata.PSObject.Properties['entryPoints']) {
        throw 'Packed independent satellite manifest does not match packed reference bytes, usage and description.'
    }
}
finally { $archive.Dispose() }

[System.IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Trellis.SatelliteProbe" Version="1.0.0" /></ItemGroup>
</Project>
'@)
[System.IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), @"
<configuration><packageSources><clear/><add key="probe" value="$feed"/></packageSources></configuration>
"@)
& dotnet restore (Join-Path $consumer 'Consumer.csproj') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Satellite-only consumer restore failed.' }
& dotnet build (Join-Path $consumer 'Consumer.csproj') --no-restore --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Satellite-only consumer build failed.' }
if ((Test-Path (Join-Path $consumer '.github')) -or (Test-Path (Join-Path $consumer '.agentdocs')) -or
    (Test-Path (Join-Path $consumer 'AGENTS.md'))) {
    throw 'Normal consumer restore/build wrote repository guidance.'
}
Write-Host 'PASS independent Trellis satellite payload and read-only consumer'
}
finally {
    if (-not $keepWorkDirectory -and (Test-Path -LiteralPath $WorkDirectory)) {
        Remove-Item -LiteralPath $WorkDirectory -Recurse -Force
    }
}
