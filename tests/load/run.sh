#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Starts the load-test stack, seeds it and runs one k6 scenario (default: mix). See README.md.
# Usage: ./run.sh [bare-proxy|jwt|apikey-ratelimit|cache|mix] [extra k6 args...]
set -eu
cd "$(dirname "$0")"

SCENARIO="${1:-mix}"
if [ "$#" -gt 0 ]; then shift; fi

./scripts/gen-jwt-keys.sh
docker compose up -d --build
./seed.sh

BASE_URL="${BASE_URL:-http://localhost:8080}"
JWT_TOKEN=$(cat .generated/jwt-token.txt)
API_KEY=$(cat .generated/api-key.txt)

k6 run "k6/${SCENARIO}.js" \
  --env BASE_URL="$BASE_URL" \
  --env JWT_TOKEN="$JWT_TOKEN" \
  --env API_KEY="$API_KEY" \
  "$@"
