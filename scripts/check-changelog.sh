#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Fails when src/**, deploy/** or templates/** changed between two revisions
# without CHANGELOG.md being touched. Usage: check-changelog.sh <base> <head>
set -euo pipefail

base="${1:?base revision}"
head="${2:?head revision}"
changed="$(git diff --name-only "$base...$head")"

if grep -qE '^(src|deploy|templates)/' <<<"$changed" && ! grep -qx 'CHANGELOG.md' <<<"$changed"; then
  echo "CHANGELOG.md must be updated when src/, deploy/ or templates/ change (or add the 'skip-changelog' label)."
  exit 1
fi
