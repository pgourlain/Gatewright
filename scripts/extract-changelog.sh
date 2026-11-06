#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Prints the body of the CHANGELOG.md section for a version (the text between its "## [x.y.z]" heading and
# the next "## [" heading), used as the GitHub Release body. A prerelease tag such as 1.2.0-rc.1 falls back to
# "[Unreleased]" when it has no section of its own. Usage: extract-changelog.sh <version> [changelog-file]
set -euo pipefail

version="${1:?version (without the leading v)}"
file="${2:-$(dirname "$0")/../CHANGELOG.md}"

extract() {
  awk -v heading="## [$1]" '
    index($0, heading) == 1 { found = 1; next }
    found && /^## \[/ { exit }
    found && /^\[[^]]+\]: / { exit }
    found { print }
  ' "$file"
}

body="$(extract "$version")"
if [[ -z "${body//[[:space:]]/}" && "$version" == *-* ]]; then
  body="$(extract "Unreleased")"
fi

if [[ -z "${body//[[:space:]]/}" ]]; then
  echo "No CHANGELOG.md content found for version $version." >&2
  exit 1
fi

# Trim leading and trailing blank lines.
printf '%s\n' "$body" | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}' | sed '/./,$!d'
