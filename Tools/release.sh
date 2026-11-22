#!/usr/bin/env bash
# Builds Windows and WebGL through the open Unity Editor and pushes them to itch. Runs ReleaseBuilds.BuildAll
# (Assets/_Project/Scripts/Editor/ReleaseBuilds.cs); the same thing is in the Editor's menu under Build > Release.
# Pushing needs ItchGame set in that file.
# Usage, from the project root in Git Bash:  Tools/release.sh <version> [--no-push]
set -euo pipefail

version="${1:?usage: Tools/release.sh <version> [--no-push]}"
push=true
[ "${2:-}" = "--no-push" ] && push=false

cd "$(dirname "$0")/.."
f="$(mktemp --suffix=.cs)"
trap 'rm -f "$f"' EXIT
printf 'return ReleaseBuilds.BuildAll("%s", %s);\n' "$version" "$push" > "$f"
# another project's Editor is often open too, so name this one
unity command script eval --file "$(cygpath -w "$f")" --project-path "$(cygpath -m "$PWD")" --result-only --timeout 3600
