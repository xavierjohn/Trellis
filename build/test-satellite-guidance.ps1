[CmdletBinding()]
param([string] $WorkDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) "trellis-satellite-probe-$PID"))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $WorkDirectory 'satellite'
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
$probe = Join-Path $WorkDirectory 'reader'
$unrelated = Join-Path $WorkDirectory 'unrelated'
if (Test-Path -LiteralPath $WorkDirectory) { throw "Probe directory already exists: $WorkDirectory" }
New-Item -ItemType Directory -Path (Join-Path $source 'trellis'), $feed, $consumer, $probe, $unrelated -Force | Out-Null

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
  <Import Project="$root\Directory.Build.targets" />
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

$reference = Join-Path $source 'trellis\trellis-api-satelliteprobe.md'
[System.IO.File]::WriteAllText($reference, "# Independent satellite`n", [System.Text.UTF8Encoding]::new($true))
[System.IO.File]::WriteAllText((Join-Path $source 'Satellite.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Trellis.SatelliteProbe</PackageId>
    <Version>1.0.0</Version>
    <TrellisApiRefName>satelliteprobe</TrellisApiRefName>
    <TrellisPublishSatelliteGuidance>true</TrellisPublishSatelliteGuidance>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <None Include="trellis\trellis-api-satelliteprobe.md" Pack="true" PackagePath="trellis/" />
  </ItemGroup>
  <Import Project="$root\build\Trellis.ApiReference.Payload.targets" />
</Project>
"@)

& dotnet pack (Join-Path $source 'Satellite.csproj') -o $feed --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Independent satellite pack failed.' }
$package = Join-Path $feed 'Trellis.SatelliteProbe.1.0.0.nupkg'
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $manifest = $archive.GetEntry('guidance/reference-manifest.json')
    $document = $archive.GetEntry('trellis/trellis-api-satelliteprobe.md')
    if ($null -eq $manifest -or $null -eq $document) {
        throw 'Independent satellite must pack both its reference and its guidance manifest.'
    }
    $manifestStream = [System.IO.StreamReader]::new($manifest.Open())
    try { $metadata = $manifestStream.ReadToEnd() | ConvertFrom-Json }
    finally { $manifestStream.Dispose() }
    $documentStream = [System.IO.MemoryStream]::new()
    try {
        $document.Open().CopyTo($documentStream)
        $digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($documentStream.ToArray()))
    }
    finally { $documentStream.Dispose() }
    if ($metadata.schemaVersion -ne 1 -or
        $metadata.documents.Count -ne 1 -or
        $metadata.documents[0].path -ne 'trellis/trellis-api-satelliteprobe.md' -or
        $metadata.documents[0].sha256 -ne $digest.ToLowerInvariant() -or
        $metadata.entryPoints.Count -ne 1 -or
        $metadata.entryPoints[0] -ne 'trellis/trellis-api-satelliteprobe.md') {
        throw 'Packed independent satellite manifest does not match packed reference bytes and entry point.'
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
if ((Test-Path (Join-Path $consumer '.github')) -or (Test-Path (Join-Path $consumer '.trellis')) -or
    (Test-Path (Join-Path $consumer 'AGENTS.md'))) {
    throw 'Normal consumer restore/build wrote repository guidance.'
}

$readerProject = Join-Path $root 'Trellis.Guidance.Reader\src\Trellis.Guidance.Reader.csproj'
[System.IO.File]::WriteAllText((Join-Path $probe 'Reader.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="$readerProject" /></ItemGroup>
</Project>
"@)
[System.IO.File]::WriteAllText((Join-Path $probe 'Program.cs'), @'
using System;
using System.Linq;
using Trellis.Guidance.Reader;
var result = GuidanceReader.Discover([args[0]]);
var package = result.Packages.Single(p => p.PackageId == "Trellis.SatelliteProbe");
if (!result.IsSuccessful || package.Status != GuidanceStatus.Valid ||
    package.Contribution?.Documents.Single().Identity.PackagePath != "trellis/trellis-api-satelliteprobe.md" ||
    package.Contribution.EntryPoints.Single() != "trellis/trellis-api-satelliteprobe.md")
    throw new Exception($"Satellite-only reader failure: {package.Status}: {package.Diagnostic}");
Console.WriteLine("PASS satellite-only package discovered from restored assets.");
'@)
& dotnet run --project (Join-Path $probe 'Reader.csproj') -- (Join-Path $consumer 'obj\project.assets.json')
if ($LASTEXITCODE -ne 0) { throw 'Satellite-only reader rejected packed reference.' }

& dotnet pack (Join-Path $root 'Trellis.AgentContext\src\Trellis.AgentContext.csproj') `
    -c Release --no-restore -o $feed --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Local CLI pack failed.' }
$tool = Get-ChildItem -LiteralPath $feed -Filter 'Trellis.AgentContext.*.nupkg' | Select-Object -First 1
if (-not $tool) { throw 'Packed local CLI is missing.' }
$toolVersion = $tool.BaseName.Substring('Trellis.AgentContext.'.Length)
& git -C $consumer init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Scratch consumer Git init failed.' }
& dotnet new tool-manifest --output (Join-Path $consumer '.config')
if ($LASTEXITCODE -ne 0) { throw 'Local tool manifest creation failed.' }
& dotnet tool install Trellis.AgentContext --version $toolVersion --add-source $feed `
    --tool-manifest (Join-Path $consumer '.config\dotnet-tools.json')
if ($LASTEXITCODE -ne 0) { throw 'Packed local CLI installation failed.' }
Push-Location $consumer
try {
    & dotnet tool run trellis agent init Consumer.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Satellite-only CLI init failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $consumer '.trellis\api-reference\trellis-api-satelliteprobe.md') -PathType Leaf)) {
        throw 'Satellite-only CLI did not install the reference.'
    }
    & dotnet tool run trellis agent check
    if ($LASTEXITCODE -ne 0) { throw 'Satellite-only CLI check failed.' }
    & dotnet tool run trellis agent remove
    if ($LASTEXITCODE -ne 0) { throw 'Satellite-only CLI remove failed.' }
}
finally { Pop-Location }
Write-Host "PASS independent satellite layout at $WorkDirectory"
