# shellcheck shell=bash
# Shared by the image cleanup scripts, sourced rather than run. They run in GitHub Actions, where GITHUB_REPOSITORY
# names the package and GH_TOKEN may delete its versions.
#
# Each tag names an image index, whose platform image and provenance are untagged versions of their own, so deleting
# an image means deleting those by digest too.

image="ghcr.io/${GITHUB_REPOSITORY,,}"
versions="/users/${GITHUB_REPOSITORY%%/*}/packages/container/${image##*/}/versions"

# Writes every version of the package, one per line: id, digest, created, updated and its tags joined by commas.
list_versions() {
  gh api --paginate "$versions" \
    --jq '.[] | [.id, .name, .created_at, .updated_at, (.metadata.container.tags | join(","))] | @tsv'
}

# Writes the digests an image index lists. Takes an image reference, by tag or by digest.
children() {
  docker buildx imagetools inspect --raw "$1" < /dev/null | jq -r '.manifests[]?.digest'
}

# The pull request cleanup and the prune job can overlap, so a version already deleted counts as done.
delete_version() {
  local error
  if ! error=$(gh api -X DELETE "$versions/$1" 2>&1 > /dev/null < /dev/null); then
    [[ $error == *'(HTTP 404)'* ]] || { echo "$error" >&2; return 1; }
  fi
}
