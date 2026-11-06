#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Imports catalog.json, creates one consumer/subscription on the "load" product, mints its
# API key (used by the apikey-ratelimit and mix scenarios) and publishes to "dev".
set -eu
cd "$(dirname "$0")"

ADMIN="${ADMIN_URL:-http://localhost:8080}/admin/v1"
json() { curl -fsS -X "$1" "$ADMIN$2" -H 'content-type: application/json' ${3:+-d "$3"}; }

until curl -fsS "$ADMIN/environments" >/dev/null 2>&1; do echo "waiting for the gateway..."; sleep 2; done

curl -fsS -X POST "$ADMIN/import" -H 'content-type: application/json' --data-binary @catalog.json >/dev/null

json PUT /products/load/plans/default '{"name":"default","description":"Load test plan"}' >/dev/null || true
json POST /consumers '{"name":"k6","contact":"load-test@example.invalid"}' >/dev/null || true
json POST /subscriptions '{"name":"k6-load","consumer":"k6","product":"load","plan":"default"}' >/dev/null || true

key=$(json POST /subscriptions/k6-load/keys/primary | sed 's/.*"key":"\([^"]*\)".*/\1/')

revision=$(curl -fsS "$ADMIN/revisions" | grep -o '"number":[0-9]*' | head -1 | cut -d: -f2)
json POST /environments/dev/publications "{\"revision\":$revision}" >/dev/null

mkdir -p .generated
printf '%s' "$key" > .generated/api-key.txt
sleep 2   # the gateway polls publications every second

echo "published revision $revision"
echo "api key: $key"
