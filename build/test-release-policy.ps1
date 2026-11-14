<#
.SYNOPSIS
    Exercises required CI gates and trusted package promotion.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'release-policy.ps1')

$repository = 'xavierjohn/Trellis'
$commit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$runId = '12345678901'
$version = '3.0.0-alpha.1'
$caseCount = 0
$work = Join-Path ([IO.Path]::GetTempPath()) "trellis-release-policy-$PID-$([Guid]::NewGuid().ToString('N'))"
$utf8 = [Text.UTF8Encoding]::new($false)

function Invoke-PolicyCase {
    param([string] $Name, [scriptblock] $Assert, [string] $ExpectedFailure)

    try {
        & $Assert | Out-Null
    }
    catch {
        if (-not $ExpectedFailure -or -not $_.Exception.Message.Contains($ExpectedFailure, [StringComparison]::Ordinal)) {
            throw "'$Name' failed unexpectedly: $($_.Exception.Message)"
        }
        $script:caseCount++
        Write-Host "PASS $Name"
        return
    }

    if ($ExpectedFailure) {
        throw "'$Name' accepted an invalid release."
    }

    $script:caseCount++
    Write-Host "PASS $Name"
}

function New-BuildFixture {
    return @{
        id = $runId
        path = '.github/workflows/build.yml'
        event = 'push'
        head_branch = 'main'
        head_sha = $commit
        status = 'completed'
        conclusion = 'success'
        repository = @{ full_name = $repository }
        head_repository = @{ full_name = $repository }
    }
}

function Invoke-BuildCase {
    param(
        [string] $Name,
        [scriptblock] $Change,
        [string] $ExpectedFailure,
        [string] $DispatchRef = 'refs/heads/main',
        [string] $DispatchCommit = $commit
    )

    $run = New-BuildFixture
    $jobs = @(@{
        name = 'build'
        conclusion = 'success'
        steps = @(@{ name = 'Require all quality gates'; conclusion = 'success' })
    })
    if ($Change) {
        & $Change $run $jobs
    }
    Invoke-PolicyCase $Name {
        Assert-ReleaseBuild -Run $run -Jobs $jobs -Repository $repository -RunId $runId `
            -DispatchRef $DispatchRef -DispatchCommit $DispatchCommit
    } $ExpectedFailure
}

function gh {
    $endpoint = @($args | Where-Object { $_ -like 'repos/*' })[0]
    $global:LASTEXITCODE = 0
    if ($endpoint.Contains('/jobs?')) {
        ConvertTo-Json -InputObject $script:jobPages -Depth 10 -Compress
    }
    elseif ($endpoint.Contains('/artifacts?')) {
        ConvertTo-Json -InputObject $script:artifactPages -Depth 10 -Compress
    }
    else {
        ConvertTo-Json -InputObject $script:apiRun -Depth 10 -Compress
    }
}

function Invoke-ArtifactCase {
    param([string] $Name, [scriptblock] $Change, [string] $ExpectedFailure)

    $script:apiRun = New-BuildFixture
    $script:apiRun.html_url = "https://github.com/$repository/actions/runs/$runId"
    $script:jobPages = @(
        @{ jobs = @(@{ name = 'encoding-policy'; conclusion = 'success' }) }
        @{ jobs = @(@{
            name = 'build'
            conclusion = 'success'
            steps = @(@{ name = 'Require all quality gates'; conclusion = 'success' })
        }) }
    )
    $script:artifactPages = @(
        @{ artifacts = @(@{
            name = 'sbom-spdx'
            id = '67890'
            expired = $false
            digest = 'sha256:' + ('d' * 64)
        }) }
        @{ artifacts = @(@{
            name = 'artifact'
            id = '12345'
            expired = $false
            digest = 'sha256:' + ('c' * 64)
        }) }
    )
    if ($Change) {
        & $Change $script:artifactPages
    }
    Invoke-PolicyCase $Name {
        $actual = Get-ReleaseBuild -RunId $runId -Repository $repository `
            -DispatchRef 'refs/heads/main' -DispatchCommit $commit
        if ($actual.artifact_id -ne '12345' -or $actual.sbom_artifact_id -ne '67890' -or $actual.commit -ne $commit) {
            throw 'The selected immutable artifact or commit changed.'
        }
    } $ExpectedFailure
}

function New-FixturePackage {
    param(
        [string] $Directory,
        [string] $Id,
        [string] $PackageVersion = $version,
        [string] $PackageCommit = $commit,
        [string] $PackageRepository = $repository,
        [string] $FileId = $Id,
        [string[]] $Dependencies = @()
    )

    $dependenciesXml = ($Dependencies | ForEach-Object {
        "<dependency id=`"$_`" version=`"[$PackageVersion]`" />"
    }) -join ''
    $path = Join-Path $Directory "$FileId.$PackageVersion.nupkg"
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry("$Id.nuspec").Open(), $utf8)
        try {
            $writer.Write(@"
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$Id</id>
    <version>$PackageVersion</version>
    <repository type="git" url="https://github.com/$PackageRepository.git" commit="$PackageCommit" />
    <dependencies>$dependenciesXml</dependencies>
  </metadata>
</package>
"@)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-FixtureProject {
    param([string] $Source, [string] $Id, [string] $Properties = '')

    $directory = Join-Path $Source "$Id\src"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory "$Id.csproj"),
        "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net11.0</TargetFramework><RepositoryUrl Condition=`"'`$(RepositoryUrl)' == ''`">https://github.com/$repository.git</RepositoryUrl>$Properties</PropertyGroup></Project>", $utf8)
}

function Invoke-PackageCase {
    param(
        [string] $Name,
        [scriptblock] $Change,
        [string] $ExpectedFailure,
        [string] $ExpectedRepository = $repository
    )

    $directory = Join-Path $work ([Guid]::NewGuid().ToString('N'))
    $packages = Join-Path $directory 'packages'
    $source = Join-Path $directory 'source'
    New-Item -ItemType Directory -Path $packages | Out-Null
    New-FixtureProject $source 'Trellis.Core' '<IsPackable>true</IsPackable>'
    New-FixtureProject $source 'Trellis.Adapter' '<IsPackable>true</IsPackable>'
    New-FixtureProject $source 'Trellis.NotShipped' '<IsPackable>false</IsPackable>'
    New-FixturePackage $packages 'Trellis.Core'
    New-FixturePackage $packages 'Trellis.Adapter'
    if ($Change) {
        & $Change $packages $source
    }
    Invoke-PolicyCase $Name {
        $actual = Get-ReleasePackageVersion -PackagesDirectory $packages -RepositoryRoot $source `
            -Repository $ExpectedRepository -Commit $commit
        if ($actual -ne $version) {
            throw "Expected version $version, received $actual."
        }
    } $ExpectedFailure
}

function Invoke-MetadataDiagnosticCase {
    param([string] $Name, [int] $ExitCode, [string] $ExpectedFailure)

    $directory = Join-Path $work ([Guid]::NewGuid().ToString('N'))
    $packages = Join-Path $directory 'packages'
    $source = Join-Path $directory 'source'
    New-Item -ItemType Directory -Path $packages | Out-Null
    New-FixtureProject $source 'Trellis.Core'
    New-FixturePackage $packages 'Trellis.Core'

    function dotnet {
        $command = @"
[Console]::Error.WriteLine('Simulated SDK diagnostic')
'{"Properties":{"IsPackable":"true","PackageId":"Trellis.Core","RepositoryUrl":"https://github.com/$repository.git"}}'
exit $ExitCode
"@
        & (Get-Process -Id $PID).Path -NoProfile -NonInteractive -Command $command
        $global:LASTEXITCODE = $LASTEXITCODE
    }

    Invoke-PolicyCase $Name {
        Get-ReleasePackageVersion -PackagesDirectory $packages -RepositoryRoot $source `
            -Repository $repository -Commit $commit
    } $ExpectedFailure
}

function Invoke-PublishOrderCase {
    param([string] $Name, [scriptblock] $Change, [string] $ExpectedFailure)

    $packages = Join-Path $work ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $packages | Out-Null
    New-FixturePackage $packages 'Trellis.Asp.ApiVersioning' -Dependencies @('Trellis.Asp')
    New-FixturePackage $packages 'Trellis.Asp' -Dependencies @('Trellis.Authorization')
    New-FixturePackage $packages 'Trellis.Authorization' -Dependencies @('Trellis.Core')
    New-FixturePackage $packages 'Trellis.Core'
    if ($Change) {
        & $Change $packages
    }
    Invoke-PolicyCase $Name {
        $emitted = [Collections.Generic.List[IO.FileInfo]]::new()
        try {
            Get-ReleasePublishOrder -PackagesDirectory $packages |
                ForEach-Object { $emitted.Add($_) }
        }
        catch {
            if ($emitted.Count -ne 0) {
                throw 'A partial publishing order was emitted before dependency validation failed.'
            }
            throw
        }
        $expected = @('Trellis.Core', 'Trellis.Authorization', 'Trellis.Asp', 'Trellis.Asp.ApiVersioning') |
            ForEach-Object { "$_.$version.nupkg" }
        if (($emitted.Name -join ',') -cne ($expected -join ',')) {
            throw "Unexpected publishing order: $($emitted.Name -join ', ')."
        }
    } $ExpectedFailure
}

function Invoke-SbomCase {
    param([string] $Name, [scriptblock] $Change, [string] $ExpectedFailure)

    $directory = Join-Path $work ([Guid]::NewGuid().ToString('N'))
    $packages = Join-Path $directory 'packages'
    $manifestDirectory = Join-Path $directory 'spdx_3.0'
    New-Item -ItemType Directory -Path $packages, $manifestDirectory -Force | Out-Null
    New-FixturePackage $packages 'Trellis.Core'
    New-FixturePackage $packages 'Trellis.Adapter'
    $manifest = @{
        '@graph' = @(
            @{ type = 'software_Package'; name = 'Trellis'; software_packageVersion = $version }
            @{ type = 'software_Package'; name = 'Dependency' }
            foreach ($package in Get-ChildItem -LiteralPath $packages -Filter *.nupkg) {
                @{
                    type = 'software_File'
                    name = "./$($package.Name)"
                    verifiedUsing = @(@{
                        algorithm = 'sha256'
                        hashValue = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
                    })
                }
            }
        )
    }
    if ($Change) {
        & $Change $manifest
    }
    [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'),
        ($manifest | ConvertTo-Json -Depth 10), $utf8)
    Invoke-PolicyCase $Name {
        Assert-ReleaseSbom -Directory $directory -PackagesDirectory $packages -Version $version
    } $ExpectedFailure
}

try {
    Invoke-PolicyCase 'all required jobs must succeed' {
        Assert-CiJobResults @{
            'encoding-policy' = @{ result = 'success' }
            'build-and-test' = @{ result = 'success' }
            documentation = @{ result = 'success' }
        }
    }
    foreach ($job in @('encoding-policy', 'build-and-test', 'documentation')) {
        foreach ($result in @('failure', 'cancelled', 'skipped')) {
            Invoke-PolicyCase "$job $result blocks the required gate" {
                $needs = @{
                    'encoding-policy' = @{ result = 'success' }
                    'build-and-test' = @{ result = 'success' }
                    documentation = @{ result = 'success' }
                }
                $needs[$job].result = $result
                Assert-CiJobResults $needs
            } 'Required CI jobs did not succeed'
        }
    }
    Invoke-PolicyCase 'an omitted documentation dependency blocks the gate' {
        Assert-CiJobResults @{
            'encoding-policy' = @{ result = 'success' }
            'build-and-test' = @{ result = 'success' }
        }
    } 'Required CI jobs did not succeed'
    Invoke-PolicyCase 'an additional successful dependency passes the gate' {
        Assert-CiJobResults @{
            'encoding-policy' = @{ result = 'success' }
            'build-and-test' = @{ result = 'success' }
            documentation = @{ result = 'success' }
            'additional-policy' = @{ result = 'success' }
        }
    }
    foreach ($result in @('failure', 'cancelled', 'skipped')) {
        Invoke-PolicyCase "an additional $result dependency blocks the gate" {
            Assert-CiJobResults @{
                'encoding-policy' = @{ result = 'success' }
                'build-and-test' = @{ result = 'success' }
                documentation = @{ result = 'success' }
                'additional-policy' = @{ result = $result }
            }
        } 'Required CI jobs did not succeed'
    }

    Invoke-BuildCase 'successful protected main build is promotable'
    Invoke-BuildCase 'a build from a different protected branch is rejected' {
        param($run, $jobs)
        $run.head_branch = 'release/v3.0'
    } 'publishing branch'
    Invoke-BuildCase 'a stale main build is rejected' {
        param($run, $jobs)
        $run.head_sha = 'b' * 40
    } 'dispatch commit'
    Invoke-BuildCase 'successful release branch build is promotable' {
        param($run, $jobs)
        $run.head_branch = 'release/v3.0'
    } -DispatchRef 'refs/heads/release/v3.0'
    Invoke-BuildCase 'release branch suffix case remains unrestricted' {
        param($run, $jobs)
        $run.head_branch = 'release/v3.0-RC.1'
    } -DispatchRef 'refs/heads/release/v3.0-RC.1'
    foreach ($branch in @('release/v', 'release/v3', 'release/v3-rc', 'release/v3_maintenance')) {
        Invoke-BuildCase "release prefix branch $branch is promotable" {
            param($run, $jobs)
            $run.head_branch = $branch
        } -DispatchRef "refs/heads/$branch"
    }
    Invoke-BuildCase 'nested release branches are not promotable' {
        param($run, $jobs)
        $run.head_branch = 'release/v3/nested'
    } 'main or a release branch'
    foreach ($dispatchRef in @('refs/heads/MAIN', 'refs/HEADS/main', 'refs/tags/main')) {
        Invoke-BuildCase "dispatch ref $dispatchRef is rejected" -DispatchRef $dispatchRef `
            -ExpectedFailure 'publishing branch'
    }
    foreach ($branch in @('MAIN', 'Main', 'release/V3.0', 'Release/v3.0', 'RELEASE/v3.0')) {
        Invoke-BuildCase "differently cased source branch $branch is rejected" {
            param($run, $jobs)
            $run.head_branch = $branch
        } 'main or a release branch'
    }
    Invoke-BuildCase 'PR builds cannot be promoted' {
        param($run, $jobs)
        $run.event = 'pull_request'
    } 'successful push build'
    Invoke-BuildCase 'manually dispatched builds cannot be promoted' {
        param($run, $jobs)
        $run.event = 'workflow_dispatch'
    } 'successful push build'
    Invoke-BuildCase 'unfinished builds cannot be promoted' {
        param($run, $jobs)
        $run.status = 'in_progress'
    } 'successful push build'
    Invoke-BuildCase 'failed builds cannot be promoted' {
        param($run, $jobs)
        $run.conclusion = 'failure'
    } 'successful push build'
    Invoke-BuildCase 'topic branch builds cannot be promoted' {
        param($run, $jobs)
        $run.head_branch = 'fix/example'
    } 'main or a release branch'
    Invoke-BuildCase 'a different workflow cannot be promoted' {
        param($run, $jobs)
        $run.path = '.github/workflows/codeql.yml'
    } 'Build workflow'
    Invoke-BuildCase 'a fork cannot supply release artifacts' {
        param($run, $jobs)
        $run.head_repository.full_name = 'other/Trellis'
    } 'repository identity'
    Invoke-BuildCase 'a mismatched run ID is rejected' {
        param($run, $jobs)
        $run.id = '999'
    } 'run identity'
    Invoke-BuildCase 'an invalid commit is rejected' {
        param($run, $jobs)
        $run.head_sha = 'not-a-commit'
    } 'commit'
    Invoke-BuildCase 'legacy builds without the aggregate gate cannot be promoted' {
        param($run, $jobs)
        $jobs[0].steps = @(@{ name = 'Pack'; conclusion = 'success' })
    } 'aggregate quality gate'
    Invoke-BuildCase 'a skipped aggregate gate cannot be promoted' {
        param($run, $jobs)
        $jobs[0].conclusion = 'skipped'
    } 'aggregate quality gate'

    Invoke-ArtifactCase 'paginated metadata selects one immutable package artifact'
    Invoke-ArtifactCase 'source selection rejects a different protected branch' {
        param($pages)
        $script:apiRun.head_branch = 'release/v3.0'
    } 'publishing branch'
    Invoke-ArtifactCase 'source revalidation rejects a stale commit' {
        param($pages)
        $script:apiRun.head_sha = 'b' * 40
    } 'dispatch commit'
    Invoke-ArtifactCase 'expired artifacts block publication' {
        param($pages)
        $pages[1].artifacts[0].expired = $true
    } 'unexpired, digest-verified package artifact'
    Invoke-ArtifactCase 'missing artifact digests block publication' {
        param($pages)
        $pages[1].artifacts[0].digest = ''
    } 'unexpired, digest-verified package artifact'
    Invoke-ArtifactCase 'ambiguous package artifacts block publication' {
        param($pages)
        $pages[0].artifacts += $pages[1].artifacts[0].Clone()
    } 'unexpired, digest-verified package artifact'
    Invoke-ArtifactCase 'missing SBOM artifacts block publication' {
        param($pages)
        $pages[0].artifacts = @()
    } 'unexpired, digest-verified SBOM artifact'
    Invoke-ArtifactCase 'expired SBOM artifacts block publication' {
        param($pages)
        $pages[0].artifacts[0].expired = $true
    } 'unexpired, digest-verified SBOM artifact'
    Invoke-ArtifactCase 'unverified SBOM artifacts block publication' {
        param($pages)
        $pages[0].artifacts[0].digest = ''
    } 'unexpired, digest-verified SBOM artifact'

    Invoke-MetadataDiagnosticCase 'SDK stderr does not corrupt evaluated metadata' -ExitCode 0
    Invoke-MetadataDiagnosticCase 'metadata command failures retain an actionable error' -ExitCode 1 `
        -ExpectedFailure 'Could not evaluate release package metadata'

    Invoke-PackageCase 'the complete same-version package cohort is promotable'
    Invoke-PackageCase 'Build validates the configured repository rather than checkout ownership' `
        -ExpectedRepository ''
    Invoke-PackageCase 'Build rejects a package inconsistent with configured RepositoryUrl' {
        param($packages, $source)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Adapter' -PackageRepository 'other/Trellis'
    } 'package provenance' -ExpectedRepository ''
    Invoke-PackageCase 'Build accepts a renamed configured repository' {
        param($packages, $source)
        [IO.File]::WriteAllText((Join-Path $source 'Directory.Build.props'), @'
<Project><PropertyGroup><RepositoryUrl>https://github.com/renamed/Framework.git</RepositoryUrl></PropertyGroup></Project>
'@, $utf8)
        foreach ($id in @('Trellis.Core', 'Trellis.Adapter')) {
            Remove-Item -LiteralPath (Join-Path $packages "$id.$version.nupkg")
            New-FixturePackage $packages $id -PackageRepository 'renamed/Framework'
        }
    } -ExpectedRepository ''
    Invoke-PackageCase 'publish still rejects packages from a different publishing repository' `
        -ExpectedRepository 'other/Trellis' -ExpectedFailure 'package provenance'
    Invoke-PackageCase 'package IDs expand MSBuild properties' {
        param($packages, $source)
        New-FixtureProject $source 'Trellis.Adapter' '<PackageId>$(AssemblyName)</PackageId>'
    }
    Invoke-PackageCase 'packability uses the Release configuration' {
        param($packages, $source)
        New-FixtureProject $source 'Trellis.Adapter' @'
<IsPackable Condition="'$(Configuration)' == 'Debug'">false</IsPackable>
'@
    }
    Invoke-PackageCase 'conditional package IDs use the Release configuration' {
        param($packages, $source)
        New-FixtureProject $source 'Trellis.Adapter' @'
<PackageId Condition="'$(Configuration)' == 'Debug'">Trellis.DebugAdapter</PackageId>
<PackageId Condition="'$(Configuration)' == 'Release'">Trellis.Adapter</PackageId>
'@
    }
    Invoke-PackageCase 'imported properties determine packability' {
        param($packages, $source)
        New-FixtureProject $source 'Trellis.NotShipped'
        [IO.File]::WriteAllText((Join-Path $source 'Directory.Build.props'),
            '<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>', $utf8)
    }
    Invoke-PackageCase 'imported properties determine package IDs' {
        param($packages, $source)
        [IO.File]::WriteAllText((Join-Path $source 'Directory.Build.props'), @'
<Project><PropertyGroup>
  <PackageId Condition="'$(MSBuildProjectName)' == 'Trellis.Adapter'">Trellis.Renamed</PackageId>
</PropertyGroup></Project>
'@, $utf8)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Renamed'
    }
    Invoke-PackageCase 'a missing package blocks publication' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
    } 'package cohort'
    Invoke-PackageCase 'an extra package blocks publication' {
        param($packages)
        New-FixturePackage $packages 'Trellis.Extra'
    } 'package cohort'
    Invoke-PackageCase 'mixed package versions block publication' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Adapter' -PackageVersion '3.0.0-alpha.2'
    } 'same version'
    Invoke-PackageCase 'a package from another commit blocks publication' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Adapter' -PackageCommit ('b' * 40)
    } 'package provenance'
    Invoke-PackageCase 'a package from another repository blocks publication' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Adapter' -PackageRepository 'other/Trellis'
    } 'package provenance'
    Invoke-PackageCase 'a renamed package blocks publication' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Adapter.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Adapter' -FileId 'Trellis.Misnamed'
    } 'package filename'

    Invoke-PublishOrderCase 'cohort dependencies are published before their dependents'
    Invoke-PublishOrderCase 'external feed dependencies do not block publishing order' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Core.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Core' -Dependencies @('System.Text.Json')
    }
    Invoke-PublishOrderCase 'dependency IDs are matched case-insensitively' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Authorization.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Authorization' -Dependencies @('trellis.core')
    }
    Invoke-PublishOrderCase 'dependency cycles fail before any package is emitted' {
        param($packages)
        Remove-Item -LiteralPath (Join-Path $packages "Trellis.Authorization.$version.nupkg")
        New-FixturePackage $packages 'Trellis.Authorization' -Dependencies @('Trellis.Asp')
    } 'dependency cycle'

    Invoke-SbomCase 'the SBOM matches the package version and bytes'
    Invoke-SbomCase 'a fresh-source SBOM without dependencies is rejected' {
        param($manifest)
        $manifest['@graph'] = @($manifest['@graph'] | Where-Object { $_.name -ne 'Dependency' })
    } 'dependency graph'
    Invoke-SbomCase 'a mismatched SBOM version is rejected' {
        param($manifest)
        $manifest['@graph'][0].software_packageVersion = '3.0.0-alpha.2'
    } 'package version'
    Invoke-SbomCase 'an incomplete SBOM file list is rejected' {
        param($manifest)
        $manifest['@graph'] = @($manifest['@graph'] | Where-Object { $_.name -ne "./Trellis.Core.$version.nupkg" })
    } 'package files'
    Invoke-SbomCase 'an SBOM for different package bytes is rejected' {
        param($manifest)
        $manifest['@graph'][2].verifiedUsing[0].hashValue = '0' * 64
    } 'package hash'
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}

Write-Host "Release policy tests passed: $caseCount cases."
