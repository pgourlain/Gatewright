#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Imports gateway-config.json into the control plane and publishes it to the "dev" environment.
set -eu
ADMIN="${ADMIN_URL:-http://localhost:8081}/admin/v1"

until curl -fsS "$ADMIN/environments" >/dev/null 2>&1; do echo "waiting for the control plane..."; sleep 2; done

curl -fsS -X POST "$ADMIN/import" -H 'content-type: application/json' --data-binary @"${CONFIG:-gateway-config.json}" >/dev/null
revision=$(curl -fsS "$ADMIN/revisions" | grep -o '"number":[0-9]*' | head -1 | cut -d: -f2)
echo "publishing revision $revision to dev"
curl -fsS -X POST "$ADMIN/environments/dev/publications" -H 'content-type: application/json' -d "{\"revision\":$revision}"
echo
