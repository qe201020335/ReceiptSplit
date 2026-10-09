# Shared by the image cleanup scripts, which dot-source it so its settings and functions run in their scope. gh uses
# GH_TOKEN when it is set (in GitHub Actions, a token that may delete the package's versions) and your own login
# otherwise; run by hand, try -WhatIf first.
#
# Each tag names an image index, whose platform image and provenance are untagged versions of their own, so deleting
# an image means deleting those by digest too.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A failing gh, docker or git stops the script instead of handing on whatever it printed.
$PSNativeCommandUseErrorActionPreference = $true

# The package CI publishes for a repository (owner/name), as the image to pull and the API path of its versions.
function Get-GhcrPackage([string] $Repository) {
    $owner, $name = $Repository.ToLowerInvariant().Split('/')
    [pscustomobject]@{
        Image = "ghcr.io/$owner/$name"
        Versions = "/users/$owner/packages/container/$name/versions"
    }
}

# Every version of the package, with its times in Unix seconds: ConvertFrom-Json would turn date strings into
# DateTimes whose kind depends on the PowerShell version.
function Get-GhcrVersion($Package) {
    $seconds = 'sub("\\.[0-9]+"; "") | fromdateiso8601'
    $jq = ".[] | {id, digest: .name, created: (.created_at | $seconds), updated: (.updated_at | $seconds), " +
        'tags: .metadata.container.tags}'
    gh api --paginate $Package.Versions --jq $jq | ForEach-Object { $_ | ConvertFrom-Json }
}

# The digests an image index lists, given an image reference by tag or by digest. Fails rather than returning nothing
# when the manifest can't be read, since a kept image's children would then look unused.
function Get-ManifestChild([string] $Reference) {
    try {
        $manifest = (docker buildx imagetools inspect --raw $Reference) -join "`n" | ConvertFrom-Json
    } catch {
        throw "Couldn't read the manifest of $Reference`: $_"
    }
    $version = $manifest ? $manifest.PSObject.Properties['schemaVersion'] : $null
    if (-not $version -or $version.Value -ne 2) {
        throw "$Reference didn't return an image manifest."
    }
    # A platform image lists no manifests.
    $list = $manifest.PSObject.Properties['manifests']
    if ($list -and $list.Value) {
        $list.Value | ForEach-Object { $_.digest }
    }
}

# Deletes one version, or with -WhatIf says it would. The pull request cleanup and the prune job can overlap, so a
# version already deleted counts as done.
function Remove-GhcrVersion {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] $Package,
        [Parameter(Mandatory)] [ValidateRange(1, [long]::MaxValue)] [long] $Id,
        [Parameter(Mandatory)] [string] $Description
    )

    if (-not $PSCmdlet.ShouldProcess($Description, 'Delete')) {
        return
    }
    Write-Host "Deleting $Description"
    $PSNativeCommandUseErrorActionPreference = $false
    $output = (gh api -X DELETE "$($Package.Versions)/$Id" 2>&1 | ForEach-Object { $_.ToString() }) -join ' '
    if ($LASTEXITCODE -ne 0 -and $output -notmatch '\(HTTP 404\)') {
        throw "Couldn't delete $Description`: $output"
    }
    # A 404 that counted as done mustn't become the exit code of a script run through a wrapper that exits with it.
    $global:LASTEXITCODE = 0
}
