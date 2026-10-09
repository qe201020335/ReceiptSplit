#!/usr/bin/env pwsh
<#
.SYNOPSIS
Deletes a closed pull request's image along with its platform image and provenance.

.DESCRIPTION
Other images' children are untagged as well, so only this index's are deleted. An image tagged anything besides the
pull request's tag and its sha-<commit> is still in use, and is kept.

.EXAMPLE
./delete-pr-image.ps1 -Repository qe201020335/ReceiptSplit -Tag pr-28 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # The repository whose package CI publishes, as owner/name.
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository,
    # Only a pull request's tag, so a typo can't name latest or a release.
    [Parameter(Mandatory)] [ValidatePattern('^pr-[0-9]+$', Options = 'None')] [string] $Tag
)

. "$PSScriptRoot/ghcr.ps1"

$package = Get-GhcrPackage $Repository
$versions = @(Get-GhcrVersion $package)

$tagged = @($versions | Where-Object { $_.tags -ccontains $Tag })
if ($tagged.Count -eq 0) {
    Write-Host "There is no $Tag image."
    return
}
if ($tagged.Count -gt 1) {
    throw "More than one version is tagged $Tag."
}
$index = $tagged[0]
$others = @($index.tags | Where-Object { $_ -cne $Tag -and $_ -cnotlike 'sha-*' })
if ($others) {
    Write-Host "::warning::$($index.digest) is also tagged $($index.tags -join ', '), so it was kept."
    return
}

$children = @(Get-ManifestChild "$($package.Image)@$($index.digest)")
Remove-GhcrVersion $package $index.id "$Tag ($($index.digest))"
foreach ($child in $children) {
    $version = $versions | Where-Object { $_.digest -ceq $child -and $_.tags.Count -eq 0 }
    if ($version) {
        Remove-GhcrVersion $package $version.id $child
    }
}
