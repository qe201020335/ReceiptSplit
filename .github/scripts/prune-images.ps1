#!/usr/bin/env pwsh
<#
.SYNOPSIS
Keeps the package to the newest master builds and the images other tags still name.

.DESCRIPTION
An image left with only its sha-<commit> tag is kept if it is one of the newest master builds, and otherwise deleted:
older master builds, older pushes to pull requests and older runs on other branches. Every untagged version no kept
index lists goes too. Anything updated in the last hour may be a build still pushing, so it stays.

Run it from a full clone at master's tip, whose history tells master builds apart from the other images tagged only
sha-<commit>.

.EXAMPLE
./prune-images.ps1 -Repository qe201020335/ReceiptSplit -Keep 10 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # The repository whose package CI publishes, as owner/name.
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository,
    # How many master builds to keep. Without one, every master build would go.
    [Parameter(Mandatory)] [ValidateRange(1, 1000)] [int] $Keep
)

. "$PSScriptRoot/ghcr.ps1"

$package = Get-GhcrPackage $Repository
$versions = @(Get-GhcrVersion $package)

# A master build whose commit is missing from this history would look like any other image and be deleted, so the
# history has to be complete and reach the commit latest was built from.
try {
    $shallow = git rev-parse --is-shallow-repository
} catch {
    throw "Run this from a clone of the repository: $_"
}
if ($shallow -ne 'false') {
    throw "Run this from a full clone of master, not a shallow one."
}
# metadata-action's sha-<commit> tags use the first seven characters.
$master = [Collections.Generic.HashSet[string]]::new(
    [string[]] @(git rev-list --first-parent HEAD | ForEach-Object { "sha-$($_.Substring(0, 7))" }))
$latest = @($versions | Where-Object { $_.tags -ccontains 'latest' } | ForEach-Object { $_.tags } |
    Where-Object { $_ -clike 'sha-*' })
if ($latest.Count -ne 1) {
    throw "There is no latest image tagged sha-<commit>, so master's builds can't be told apart."
}
if (-not $master.Contains($latest[0])) {
    throw "latest ($($latest[0])) isn't in this history; prune from master's tip."
}

# Tagged with the sha-<commit> of a commit in master's history.
function Test-MasterBuild($Version) {
    [bool] @($Version.tags | Where-Object { $master.Contains($_) })
}

# Tagged sha-<commit> and nothing else: whatever else named it has moved on.
function Test-ShaOnly($Version) {
    $Version.tags.Count -gt 0 -and -not @($Version.tags | Where-Object { $_ -cnotlike 'sha-*' })
}

$builds = @($versions | Where-Object { Test-MasterBuild $_ } | Sort-Object created -Descending)
$old = @(
    $builds | Select-Object -Skip $Keep | Where-Object { Test-ShaOnly $_ }
    $versions | Where-Object { -not (Test-MasterBuild $_) -and (Test-ShaOnly $_) }
)

$oldIds = [Collections.Generic.HashSet[long]]::new([long[]] @($old | ForEach-Object { $_.id }))
$kept = [Collections.Generic.HashSet[string]]::new()
foreach ($version in @($versions | Where-Object { $_.tags.Count -gt 0 -and -not $oldIds.Contains($_.id) })) {
    [void] $kept.Add($version.digest)
    foreach ($child in @(Get-ManifestChild "$($package.Image)@$($version.digest)")) {
        [void] $kept.Add($child)
    }
}

$cutoff = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - 3600
$orphans = @($versions | Where-Object {
    $_.tags.Count -eq 0 -and $_.updated -lt $cutoff -and -not $kept.Contains($_.digest)
})

# A version that can't be deleted doesn't stop the rest, but fails the run.
$failed = 0
foreach ($version in $old + $orphans) {
    $description = $version.tags.Count ? "$($version.digest) ($($version.tags -join ', '))" : $version.digest
    try {
        Remove-GhcrVersion $package $version.id $description
    } catch {
        Write-Host "::error::$_"
        $failed++
    }
}
if ($failed) {
    throw "$failed of $($old.Count + $orphans.Count) versions couldn't be deleted."
}
