#!/bin/sh
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
# Generates an RSA key pair, a JWKS document (served by the "jwks" compose service) and a
# long-lived RS256 token matching the "secured" API's jwt policy in catalog.json. Development
# tooling only: the key is throwaway and regenerated on every run unless the files already exist.
set -eu
cd "$(dirname "$0")/.."

OUT="./.generated/jwks"
mkdir -p "$OUT"
KEY="$OUT/private.pem"
TOKEN_FILE="./.generated/jwt-token.txt"

ISSUER="https://load.test"
AUDIENCE="api://load"
KID="load-test"

if [ ! -f "$KEY" ]; then
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY" 2>/dev/null
fi

python3 - "$KEY" "$OUT/jwks.json" "$TOKEN_FILE" "$ISSUER" "$AUDIENCE" "$KID" <<'PY'
import base64, json, subprocess, sys, time

key_path, jwks_path, token_path, issuer, audience, kid = sys.argv[1:7]


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


# Modulus and public exponent, read from the key itself (openssl, no extra Python package).
modulus_hex = subprocess.run(
    ["openssl", "rsa", "-in", key_path, "-modulus", "-noout"],
    capture_output=True, text=True, check=True,
).stdout.strip().removeprefix("Modulus=")
n = b64url(bytes.fromhex(modulus_hex))
e = b64url((65537).to_bytes(3, "big").lstrip(b"\x00"))

jwks = {"keys": [{"kty": "RSA", "use": "sig", "alg": "RS256", "kid": kid, "n": n, "e": e}]}
with open(jwks_path, "w") as f:
    json.dump(jwks, f)

header = b64url(json.dumps({"alg": "RS256", "typ": "JWT", "kid": kid}).encode())
payload = b64url(json.dumps({
    "iss": issuer,
    "aud": audience,
    "sub": "k6-load-test",
    "iat": int(time.time()),
    "exp": int(time.time()) + 10 * 365 * 24 * 3600,
}).encode())
signing_input = f"{header}.{payload}".encode()

signature = subprocess.run(
    ["openssl", "dgst", "-sha256", "-sign", key_path],
    input=signing_input, capture_output=True, check=True,
).stdout
token = f"{header}.{payload}.{b64url(signature)}"

with open(token_path, "w") as f:
    f.write(token)
PY

echo "JWKS written to $OUT/jwks.json"
echo "Token written to $TOKEN_FILE"
