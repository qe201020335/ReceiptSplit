#!/usr/bin/env bash
# Deletes a closed pull request's image along with its platform image and provenance. Other images' children are
# untagged as well, so only this index's are deleted.
#
# Usage: delete-pr-image.sh [--dry-run] pr-<number>
set -euo pipefail
if [ "${1:-}" = --dry-run ]; then
  dry_run=1
  shift
fi
# shellcheck source-path=SCRIPTDIR
source "$(dirname "$0")/ghcr.sh"

tag=${1:-}
# Only a pull request's tag, so a typo can't name latest or a release.
[[ $tag =~ ^pr-[0-9]+$ ]] || die "Usage: delete-pr-image.sh [--dry-run] pr-<number>"
work=$(mktemp -d)
list_versions > "$work/versions.tsv"

tagged=$(awk -F '\t' -v tag="$tag" '{ n = split($5, t, ","); for (i = 1; i <= n; i++) if (t[i] == tag) print }' \
  "$work/versions.tsv")
if [ -z "$tagged" ]; then
  echo "There is no $tag image."
  exit 0
fi
[ "$(wc -l <<< "$tagged")" = 1 ] || die "More than one version is tagged $tag."
IFS=$'\t' read -r id digest _ _ tags <<< "$tagged"
# Besides its sha-<commit>, an image tagged anything else is still in use.
if tr ',' '\n' <<< "$tags" | grep -qvx -e "$tag" -e 'sha-.*'; then
  echo "::warning::$digest is also tagged $tags, so it was kept."
  exit 0
fi

kids=$(children "$image:$tag")
announce "$tag ($digest)"
delete_version "$id"
for child in $kids; do
  child_id=$(awk -F '\t' -v digest="$child" '$2 == digest && $5 == "" { print $1 }' "$work/versions.tsv")
  if [ -n "$child_id" ]; then
    announce "$child"
    delete_version "$child_id"
  fi
done
