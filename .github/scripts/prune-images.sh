#!/usr/bin/env bash
# Keeps the package to the newest master builds and the images other tags still name. Run from a full clone of
# master, since its history tells master builds apart from the other images tagged only sha-<commit>.
#
# An image left with only its sha-<commit> tag is kept if it is one of the newest master builds, and otherwise
# deleted: older master builds, older pushes to pull requests and older runs on other branches. Every untagged version
# no kept index lists goes too. Anything updated in the last hour may be a build still pushing, so it stays.
#
# Usage: prune-images.sh [--dry-run] <master builds to keep>
set -euo pipefail
if [ "${1:-}" = --dry-run ]; then
  dry_run=1
  shift
fi
# shellcheck source-path=SCRIPTDIR
source "$(dirname "$0")/ghcr.sh"

keep=${1:-}
# Without a count, every master build would go.
[[ $keep =~ ^[1-9][0-9]*$ ]] || die "Usage: prune-images.sh [--dry-run] <master builds to keep, at least 1>"
work=$(mktemp -d)
list_versions > "$work/versions.tsv"

# A master build whose commit is missing from this history would look like any other image and be deleted, so the
# history has to be complete and reach the commit latest was built from.
[ "$(git rev-parse --is-shallow-repository)" = false ] || die "Run this from a full clone of master, not a shallow one."
# metadata-action's sha-<commit> tags use the first seven characters.
git rev-list --first-parent HEAD | cut -c1-7 | sed 's/^/sha-/' > "$work/master.txt"
latest=$(awk -F '\t' '{
  n = split($5, t, ","); l = 0; s = ""
  for (i = 1; i <= n; i++) { if (t[i] == "latest") l = 1; if (t[i] ~ /^sha-/) s = t[i] }
  if (l) print s
}' "$work/versions.tsv")
[ -n "$latest" ] || die "There is no latest image tagged sha-<commit>, so master's builds can't be told apart."
grep -qx -- "$latest" "$work/master.txt" || die "latest ($latest) isn't in this history; prune from master's tip."
# Adds whether a version is a master build, and whether sha-<commit> is all it is tagged.
awk -F '\t' -v OFS='\t' 'FILENAME == ARGV[1] { master[$1]; next } {
  n = split($5, t, ","); build = 0; shaonly = n > 0
  for (i = 1; i <= n; i++) { if (t[i] in master) build = 1; if (t[i] !~ /^sha-/) shaonly = 0 }
  print $0, build, shaonly
}' "$work/master.txt" "$work/versions.tsv" > "$work/classified.tsv"
{
  awk -F '\t' '$6' "$work/classified.tsv" | sort -t $'\t' -k3,3r | tail -n +$((keep + 1)) | awk -F '\t' '$7'
  awk -F '\t' '!$6 && $7' "$work/classified.tsv"
} | cut -f 1-5 > "$work/old.tsv"

awk -F '\t' 'FILENAME == ARGV[1] { old[$1]; next } $5 != "" && !($1 in old) { print $2 }' \
  "$work/old.tsv" "$work/versions.tsv" > "$work/kept.txt"
while read -r digest; do
  children "$image@$digest"
done < "$work/kept.txt" > "$work/children.txt"
cat "$work/children.txt" >> "$work/kept.txt"

cutoff=$(date -u -d '1 hour ago' +%Y-%m-%dT%H:%M:%SZ)
awk -F '\t' -v cutoff="$cutoff" 'FILENAME == ARGV[1] { kept[$1]; next } $5 == "" && $4 < cutoff && !($2 in kept)' \
  "$work/kept.txt" "$work/versions.tsv" > "$work/orphans.tsv"

failed=0
while IFS=$'\t' read -r id digest _ _ tags; do
  announce "$digest${tags:+ ($tags)}"
  delete_version "$id" || failed=1
done < <(cat "$work/old.tsv" "$work/orphans.tsv")
exit "$failed"
