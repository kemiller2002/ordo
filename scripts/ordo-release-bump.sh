#!/usr/bin/env bash
# Bumps the Ordo release version as an attributed Praxis work item and pushes
# it, so native-release.yml can publish it (see .github/workflows/release.yml).
#
#   scripts/ordo-release-bump.sh patch|minor|major|X.Y.Z [--dry-run]
#
# distribution/package.json is the single authoritative version. This
# repository's own .echelon/toolchain.json pins the same Ordo release
# (ToolchainPinTests), so it moves in the same commit; only its ordo property
# changes and every other property (for example praxis) is kept.
#
# The bump follows the work protocol like any other change: the work item
# begins before anything is mutated, the bump is committed and pushed, a
# durable checkpoint verifies it on the remote, and the work item completes
# before the Praxis state is committed and pushed. Nothing is published here.
#
# Fails closed: a version that is not newer, or whose tag or release already
# exists, stops the bump before anything is mutated.
#
# Requires: a clean checkout of a branch with an upstream, ./praxis, python3,
# gh (authenticated), and a Git identity. Prints `version=`, `base=` and
# `head=` lines (append them to $GITHUB_OUTPUT in Actions).
set -euo pipefail

usage() { echo "usage: $0 patch|minor|major|X.Y.Z [--dry-run]" >&2; exit 2; }
# Millisecond precision: a whole-second timestamp can predate the execution
# that `work start` opened moments earlier, which validation rejects.
now() { python3 -c 'from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"))'; }
fail() { echo "ERROR $*" >&2; exit 1; }

[ "$#" -ge 1 ] && [ "$#" -le 2 ] || usage
bump="$1"
dry_run=false
if [ "$#" -eq 2 ]; then
  [ "$2" = "--dry-run" ] || usage
  dry_run=true
fi

cd "$(git rev-parse --show-toplevel)"

# The next version, derived without mutating anything.
next_version() {
  python3 - "$1" "$2" <<'PY'
import re, sys
current, bump = sys.argv[1], sys.argv[2]
core = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
parts = core.match(current)
if not parts:
    sys.exit(f"current version {current} is not X.Y.Z")
major, minor, patch = map(int, parts.groups())
nxt = {"major": f"{major + 1}.0.0", "minor": f"{major}.{minor + 1}.0", "patch": f"{major}.{minor}.{patch + 1}"}.get(bump, bump)
if not core.match(nxt):
    sys.exit(f'"{bump}" is not patch, minor, major or X.Y.Z')
if tuple(map(int, nxt.split("."))) <= (major, minor, patch):
    sys.exit(f"{nxt} is not newer than {current}")
sys.stdout.write(nxt)
PY
}

# Sets the authoritative version and Ordo's own pin, changing nothing else in
# either file.
set_version() {
  python3 - "$1" <<'PY'
import json, sys
for path, key in (("distribution/package.json", "version"), (".echelon/toolchain.json", "ordo")):
    with open(path, encoding="utf-8") as source:
        document = json.load(source)
    document[key] = sys.argv[1]
    with open(path, "w", encoding="utf-8") as target:
        target.write(json.dumps(document, indent=2, ensure_ascii=False) + "\n")
PY
}

current="$(python3 -c 'import json; print(json.load(open("distribution/package.json"))["version"])')"
version="$(next_version "$current" "$bump")"
id="RELEASE-${version//./-}"

# Released versions are immutable: never reuse a tag or a release.
if git ls-remote --exit-code --tags origin "refs/tags/v${version}" >/dev/null 2>&1; then
  fail "v${version} is already tagged; released versions are immutable"
fi
if gh release view "v${version}" >/dev/null 2>&1; then
  fail "a v${version} release already exists; released versions are immutable"
fi

echo "releasing ${current} -> ${version} as ${id}" >&2

if [ "$dry_run" = true ]; then
  echo "version=${version}"
  exit 0
fi

branch="$(git symbolic-ref --quiet --short HEAD)" || fail "HEAD is detached; check out the release branch"
git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' >/dev/null 2>&1 || fail "branch '$branch' has no upstream"
[ -z "$(git status --porcelain)" ] || fail "the working tree is not clean"
git fetch --quiet origin "$branch"
[ "$(git rev-parse HEAD)" = "$(git rev-parse '@{upstream}')" ] || fail "'$branch' is not at its upstream head; pull or push first"

base="$(git rev-parse HEAD)"

# Begin the work item before mutating anything.
./praxis add "Release Ordo ${version}" --id "$id" --type mechanical \
  --description "Bump distribution/package.json and the self-hosting .echelon/toolchain.json ordo pin from ${current} to ${version} so native-release.yml releases it." >/dev/null
./praxis work backlog-transition --action ready --id "$id" --occurred-at "$(now)" >/dev/null
./praxis work start --id "$id" --type mechanical --occurred-at "$(now)" >/dev/null

set_version "$version"
git add distribution/package.json .echelon/toolchain.json .ros
git commit --quiet -m "${id}: release Ordo ${version}"
git push --quiet origin "HEAD:${branch}"

./praxis work checkpoint --id "$id" --occurred-at "$(now)" \
  --summary "Bumped the Ordo version and its toolchain pin from ${current} to ${version}" \
  --next-action "Run final completion transition" >&2
./praxis work complete --id "$id" --occurred-at "$(now)" >/dev/null
./praxis registry build >/dev/null
./praxis validate >&2

git add -A
git commit --quiet -m "${id}: final checkpoint and completion"
git push --quiet origin "HEAD:${branch}"

echo "version=${version}"
echo "base=${base}"
echo "head=$(git rev-parse HEAD)"
