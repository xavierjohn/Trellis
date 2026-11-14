<#
.SYNOPSIS
    Validates required CI results and the provenance of release packages.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-CiJobResults {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Needs)

    $names = @('encoding-policy', 'build-and-test', 'documentation') + @($Needs.Keys)
    $failed = @(
        foreach ($name in ($names | Select-Object -Unique)) {
            if (-not $Needs.Contains($name) -or $Needs[$name].result -ne 'success') {
                $name
            }
        }
    )
    if ($failed.Count -ne 0) {
        throw "Required CI jobs did not succeed: $($failed -join ', ')."
    }
}

function Assert-ReleaseBuild {
    param(
        [Parameter(Mandatory)] [hashtable] $Run,
        [Parameter(Mandatory)] [object[]] $Jobs,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $RunId
    )

    if ([string] $Run.id -ne $RunId) {
        throw 'The source build run identity does not match the request.'
    }
    if ($Run.repository.full_name -ine $Repository -or $Run.head_repository.full_name -ine $Repository) {
        throw 'The source build repository identity does not match this repository.'
    }
    if ($Run.path -ne '.github/workflows/build.yml') {
        throw 'Packages must come from the Build workflow.'
    }
    if ($Run.event -ne 'push' -or $Run.status -ne 'completed' -or $Run.conclusion -ne 'success') {
        throw 'Packages must come from a completed, successful push build.'
    }
    if ($Run.head_branch -cne 'main' -and $Run.head_branch -cnotmatch '^release/v[0-9]+\.[0-9]+[A-Za-z0-9.-]*$') {
        throw 'Packages must come from main or a release branch.'
    }
    if ($Run.head_sha -notmatch '^[0-9a-f]{40}$') {
        throw 'The source build commit is invalid.'
    }

    $gates = @($Jobs | Where-Object { $_.name -eq 'build' })
    if ($gates.Count -ne 1 -or $gates[0].conclusion -ne 'success' -or
        @($gates[0].steps | Where-Object {
            $_.name -eq 'Require all quality gates' -and $_.conclusion -eq 'success'
        }).Count -ne 1) {
        throw 'The source build must include a successful aggregate quality gate.'
    }
}

function Get-ReleaseBuild {
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[0-9]+$')] [string] $RunId,
        [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$')] [string] $Repository
    )

    $endpoint = "repos/$Repository/actions/runs/$RunId"
    $output = & gh api --hostname github.com $endpoint
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the source build.'
    }
    $run = $output | ConvertFrom-Json -AsHashtable

    $output = & gh api --hostname github.com "$endpoint/jobs?per_page=100" --paginate --slurp
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the source build jobs.'
    }
    $jobs = @($output | ConvertFrom-Json -AsHashtable | ForEach-Object { $_.jobs })
    Assert-ReleaseBuild -Run $run -Jobs $jobs -Repository $Repository -RunId $RunId

    $output = & gh api --hostname github.com "$endpoint/artifacts?per_page=100" --paginate --slurp
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the source build artifacts.'
    }
    $artifacts = @($output | ConvertFrom-Json -AsHashtable | ForEach-Object { $_.artifacts })
    $selected = @{}
    foreach ($name in @('artifact', 'sbom-spdx')) {
        $matchingArtifacts = @($artifacts | Where-Object { $_.name -eq $name })
        if ($matchingArtifacts.Count -ne 1 -or $matchingArtifacts[0].expired -or
            [string] $matchingArtifacts[0].id -notmatch '^[0-9]+$' -or
            $matchingArtifacts[0].digest -notmatch '^sha256:[0-9a-f]{64}$') {
            $kind = if ($name -eq 'artifact') { 'package' } else { 'SBOM' }
            throw "The source build must have one unexpired, digest-verified $kind artifact."
        }
        $selected[$name] = [string] $matchingArtifacts[0].id
    }

    return @{
        run_id = $RunId
        commit = $run.head_sha
        artifact_id = $selected['artifact']
        sbom_artifact_id = $selected['sbom-spdx']
        url = $run.html_url
    }
}

function Get-ReleasePackageVersion {
    param(
        [Parameter(Mandatory)] [string] $PackagesDirectory,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [ValidatePattern('^[0-9a-f]{40}$')] [string] $Commit
    )

    $expectedIds = @(
        Get-ChildItem -LiteralPath $RepositoryRoot -Directory -Filter 'Trellis.*' |
            ForEach-Object {
                $src = Join-Path $_.FullName 'src'
                if (Test-Path -LiteralPath $src -PathType Container) {
                    Get-ChildItem -LiteralPath $src -File -Filter '*.csproj'
                }
            } | ForEach-Object {
                [xml] $project = [IO.File]::ReadAllText($_.FullName)
                if ($project.SelectSingleNode('//IsPackable')?.InnerText -ne 'false') {
                    $id = $project.SelectSingleNode('//PackageId')?.InnerText
                    if (-not $id) {
                        $id = $_.BaseName
                    }
                    $id
                }
            }
    )
    $packages = @(Get-ChildItem -LiteralPath $PackagesDirectory -File -Filter '*.nupkg')
    if ($expectedIds.Count -eq 0 -or $packages.Count -ne $expectedIds.Count -or
        @(Get-ChildItem -LiteralPath $PackagesDirectory -Force).Count -ne $packages.Count) {
        throw 'The release package cohort must exactly match the packable source projects.'
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $versions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in $packages) {
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspecs = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
            if ($nuspecs.Count -ne 1) {
                throw "Package $($package.Name) must contain exactly one root nuspec."
            }
            $reader = [IO.StreamReader]::new($nuspecs[0].Open())
            try {
                [xml] $nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
            $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
            $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
            $source = $metadata.SelectSingleNode('*[local-name()="repository"]')
            if ($id -notin $expectedIds -or -not $seen.Add($id)) {
                throw "Unexpected or duplicated member of the release package cohort: $id."
            }
            if ($version -notmatch '^[0-9A-Za-z.+-]+$' -or $package.Name -cne "$id.$version.nupkg") {
                throw "Invalid release package filename: $($package.Name)."
            }
            $url = if ($source) { $source.GetAttribute('url').TrimEnd('/') } else { '' }
            if (-not $source -or $source.GetAttribute('commit') -cne $Commit -or
                ($url -ine "https://github.com/$Repository" -and $url -ine "https://github.com/$Repository.git")) {
                throw "Invalid release package provenance: $($package.Name)."
            }
            $versions.Add($version) | Out-Null
        }
        finally {
            $archive.Dispose()
        }
    }
    if ($versions.Count -ne 1) {
        throw 'Every release package must have the same version.'
    }

    return @($versions)[0]
}

function Assert-ReleaseSbom {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $PackagesDirectory,
        [Parameter(Mandatory)] [string] $Version
    )

    $path = Join-Path (Join-Path $Directory 'spdx_3.0') 'manifest.spdx.json'
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $graph = $manifest['@graph']
    $root = @($graph | Where-Object { $_.type -eq 'software_Package' -and $_.name -eq 'Trellis' })
    if ($root.Count -ne 1 -or $root[0].software_packageVersion -cne $Version) {
        throw 'The SBOM must match the release package version.'
    }
    if (@($graph | Where-Object { $_.type -eq 'software_Package' }).Count -le 1) {
        throw 'The SBOM must retain the resolved dependency graph.'
    }

    $files = @($graph | Where-Object { $_.type -eq 'software_File' })
    $packages = @(Get-ChildItem -LiteralPath $PackagesDirectory -File -Filter *.nupkg)
    if ($files.Count -ne $packages.Count) {
        throw 'The SBOM must list exactly the release package files.'
    }
    foreach ($package in $packages) {
        $file = @($files | Where-Object { $_.name -ceq "./$($package.Name)" })
        if ($file.Count -ne 1) {
            throw "The SBOM must list the release package files: $($package.Name)."
        }
        $hashes = @($file[0].verifiedUsing | Where-Object { $_.algorithm -eq 'sha256' })
        $hash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
        if ($hashes.Count -ne 1 -or $hashes[0].hashValue -ine $hash) {
            throw "The SBOM package hash does not match: $($package.Name)."
        }
    }
}
