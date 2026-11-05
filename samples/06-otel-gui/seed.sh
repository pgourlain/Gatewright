#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Imports catalog.json into the gateway (database mode) and publishes it to "dev".
set -eu
ADMIN="${ADMIN_URL:-http://localhost:8080}/admin/v1"

until curl -fsS "$ADMIN/environments" >/dev/null 2>&1; do echo "waiting for the gateway..."; sleep 2; done

curl -fsS -X POST "$ADMIN/import" -H 'content-type: application/json' --data-binary @"${CATALOG:-catalog.json}" >/dev/null
revision=$(curl -fsS "$ADMIN/revisions" | grep -o '"number":[0-9]*' | head -1 | cut -d: -f2)
curl -fsS -X POST "$ADMIN/environments/dev/publications" -H 'content-type: application/json' -d "{\"revision\":$revision}" >/dev/null
echo "published revision $revision"
sleep 2   # the gateway polls publications every second
