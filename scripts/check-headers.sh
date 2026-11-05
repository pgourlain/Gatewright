#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Verifies the license header on non-C# source files (.razor, .proto, .sh, .ps1).
# C# files are enforced by the compiler (IDE0073 as error).
set -euo pipefail

cd "$(dirname "$0")/.."
status=0

while IFS= read -r -d '' file; do
  case "$file" in
    *.razor) prefix='@*' ;;
    *) prefix='' ;;
  esac
  head_lines="$(head -n 6 "$file")"
  if ! grep -q 'SPDX-License-Identifier: MIT' <<<"$head_lines" || ! grep -q 'Copyright (c) Gatewright contributors.' <<<"$head_lines"; then
    echo "Missing license header: $file"
    status=1
  fi
  : "$prefix"
done < <(git ls-files -z -- '*.razor' '*.proto' '*.sh' '*.ps1' ':!:**/obj/**' ':!:**/bin/**')

exit $status
