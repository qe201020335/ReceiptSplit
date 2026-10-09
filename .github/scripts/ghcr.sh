# shellcheck shell=bash
# Shared by the image cleanup scripts, sourced rather than run. In GitHub Actions, GITHUB_REPOSITORY names the package
# and GH_TOKEN may delete its versions. Run by hand, set GITHUB_REPOSITORY (owner/name) and pass --dry-run first; gh
# uses your own login unless GH_TOKEN is set.
#
# Each tag names an image index, whose platform image and provenance are untagged versions of their own, so deleting
# an image means deleting those by digest too.

die() {
  echo "$*" >&2
  exit 1
}

[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || die "GITHUB_REPOSITORY must be owner/name."
image="ghcr.io/${GITHUB_REPOSITORY,,}"
versions="/users/${GITHUB_REPOSITORY%%/*}/packages/container/${image##*/}/versions"
dry_run=${dry_run:-0}

# Writes every version of the package, one per line: id, digest, created, updated and its tags joined by commas.
list_versions() {
  gh api --paginate "$versions" \
    --jq '.[] | [.id, .name, .created_at, .updated_at, (.metadata.container.tags | join(","))] | @tsv'
}

# Writes the digests an image index lists. Takes an image reference, by tag or by digest. Fails rather than writing
# nothing when the manifest can't be read, since a kept image's children would then look unused.
children() {
  local raw
  raw=$(docker buildx imagetools inspect --raw "$1" < /dev/null) || die "Couldn't read the manifest of $1."
  jq -e '.schemaVersion == 2' <<< "$raw" > /dev/null || die "$1 didn't return an image manifest."
  jq -r '.manifests[]?.digest' <<< "$raw"
}

# Says what is about to be deleted, or with --dry-run what would be.
announce() {
  if [ "$dry_run" = 1 ]; then
    echo "Would delete $*"
  else
    echo "Deleting $*"
  fi
}

# The pull request cleanup and the prune job can overlap, so a version already deleted counts as done.
delete_version() {
  local error
  [[ $1 =~ ^[0-9]+$ ]] || die "Not a package version id: '$1'."
  if [ "$dry_run" = 1 ]; then
    return 0
  fi
  if ! error=$(gh api -X DELETE "$versions/$1" 2>&1 > /dev/null < /dev/null); then
    [[ $error == *'(HTTP 404)'* ]] || { echo "$error" >&2; return 1; }
  fi
}
