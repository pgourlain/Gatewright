#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Turns "## [Unreleased]" of CHANGELOG.md into "## [x.y.z] - YYYY-MM-DD", opens a fresh empty "[Unreleased]"
# section and updates the comparison links at the bottom (docs/release.md, step 2).
# Usage: prepare-release.sh <x.y.z> [changelog-file] [date]
set -euo pipefail

version="${1:?version x.y.z (stable releases only; prereleases are tagged without touching the changelog)}"
file="${2:-$(dirname "$0")/../CHANGELOG.md}"
date="${3:-$(date -u +%Y-%m-%d)}"

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "'$version' is not a stable x.y.z version." >&2
  exit 1
fi

python3 - "$version" "$file" "$date" <<'PY'
import re, sys

version, path, date = sys.argv[1:4]
text = open(path, encoding="utf-8").read()

if re.search(rf"^## \[{re.escape(version)}\]", text, re.M):
    sys.exit(f"CHANGELOG.md already has a section for {version}.")

m = re.search(r"^## \[Unreleased\]\n(.*?)(?=^## \[|^\[[^\]]+\]: )", text, re.M | re.S)
if not m:
    sys.exit("No '## [Unreleased]' section found.")
body = m.group(1)
if not re.search(r"^- ", body, re.M):
    sys.exit("The [Unreleased] section has no entry: nothing to release.")

# Empty subsections are noise in a released section.
body = re.sub(r"^### \w+\n\n(?=### |\Z)", "", body, flags=re.M)

headings = ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"]
fresh = "## [Unreleased]\n\n" + "\n".join(f"### {h}\n" for h in headings) + "\n"
released = f"## [{version}] - {date}\n" + body
text = text[: m.start()] + fresh + released + text[m.end():]

# Comparison links.
prev = re.search(r"^## \[(\d+\.\d+\.\d+)\]", text[text.index(f"## [{version}]") + 1:], re.M)
base = None
unreleased_link = re.search(r"^\[Unreleased\]: (https://github\.com/[^/]+/[^/]+)/", text, re.M)
if unreleased_link:
    base = unreleased_link.group(1)
if base:
    new_link = f"[{version}]: {base}/compare/v{prev.group(1)}...v{version}" if prev else f"[{version}]: {base}/releases/tag/v{version}"
    text = re.sub(r"^\[Unreleased\]: .*$", f"[Unreleased]: {base}/compare/v{version}...HEAD\n{new_link}", text, flags=re.M)
else:
    print("Warning: no [Unreleased] link found; comparison links not updated.", file=sys.stderr)

open(path, "w", encoding="utf-8").write(text)
print(f"CHANGELOG.md: [Unreleased] -> [{version}] - {date}")
PY
