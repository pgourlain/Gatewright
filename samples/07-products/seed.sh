#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Builds the catalogue: products and APIs (catalog.json), plans, customers, subscriptions and one API key per customer; then publishes to "dev".
set -eu
ADMIN="${ADMIN_URL:-http://localhost:8080}/admin/v1"
json() { curl -fsS -X "$1" "$ADMIN$2" -H 'content-type: application/json' ${3:+-d "$3"}; }

until curl -fsS "$ADMIN/environments" >/dev/null 2>&1; do echo "waiting for the gateway..."; sleep 2; done

curl -fsS -X POST "$ADMIN/import" -H 'content-type: application/json' --data-binary @"${CATALOG:-catalog.json}" >/dev/null

json PUT /products/starter/plans/free    '{"name":"free","description":"Evaluation, 5 requests per minute on orders"}' >/dev/null
json PUT /products/premium/plans/business '{"name":"business","description":"Production, orders and analytics"}' >/dev/null

json POST /consumers '{"name":"acme","contact":"dev@acme.example"}' >/dev/null
json POST /consumers '{"name":"globex","contact":"it@globex.example"}' >/dev/null
json POST /subscriptions '{"name":"acme-starter","consumer":"acme","product":"starter","plan":"free"}' >/dev/null
json POST /subscriptions '{"name":"globex-premium","consumer":"globex","product":"premium","plan":"business"}' >/dev/null

acme=$(json POST /subscriptions/acme-starter/keys/primary | sed 's/.*"key":"\([^"]*\)".*/\1/')
globex=$(json POST /subscriptions/globex-premium/keys/primary | sed 's/.*"key":"\([^"]*\)".*/\1/')

revision=$(curl -fsS "$ADMIN/revisions" | grep -o '"number":[0-9]*' | head -1 | cut -d: -f2)
json POST /environments/dev/publications "{\"revision\":$revision}" >/dev/null

echo "published revision $revision"
echo "acme   (starter) key: $acme"
echo "globex (premium) key: $globex"
sleep 2   # the gateway polls publications every second
