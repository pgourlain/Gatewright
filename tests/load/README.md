# Load scenarios (phase 14.1)

[k6](https://k6.io) scripts against a throwaway compose stack (gateway in `all` mode,
database config source, Redis-backed shared rate limit), covering the scenarios listed in
`tasks.md` 14.1: bare proxy, `jwt`, `apiKey` + Redis `rateLimit`, `cache`, and a realistic mix.

k6 was picked over NBomber: no dependency on the .NET build, and it is the de facto tool for
this kind of HTTP load scenario — a separate process driving the gateway from the outside, the
same way a production load balancer would.

## Prerequisites

- Docker (or Podman/Colima) with the `compose` plugin.
- [k6](https://k6.io/docs/get-started/installation/) on the host running the scripts.
- `openssl` and `python3` (used once to mint the JWKS key pair and a long-lived test token;
  not a runtime dependency of Gatewright itself).

## Running

```bash
./run.sh                      # runs the "mix" scenario
./run.sh bare-proxy
./run.sh jwt
./run.sh apikey-ratelimit
./run.sh cache
./run.sh mix --out json=result.json   # extra args are passed through to `k6 run`
```

`run.sh` generates the JWKS key pair and test token once (`scripts/gen-jwt-keys.sh`, idempotent;
delete `.generated/` to force a new key), starts the stack (`docker compose up -d --build`), seeds
one product/subscription/API key (`seed.sh`) and runs the chosen k6 script against
`http://localhost:8080` (override with `BASE_URL`). `VUS` and `DURATION` env vars tune load
(defaults: 50 VUs / 30s per scenario, 100 VUs / 60s for `mix`). The stack is left running after a
scenario so several can be run back to back; `docker compose down -v` tears it down.

`.generated/` (private key, JWKS, token, API key) is gitignored: it is throwaway dev-only
material regenerated on every fresh run, never a real secret.

## Scenarios

| Script | Route | Exercises |
|---|---|---|
| `bare-proxy.js` | `/bare` | no policy — pipeline baseline |
| `jwt.js` | `/secured` | RS256 validation against the cached JWKS (no network call on the hot path) |
| `apikey-ratelimit.js` | `/limited` | hashed key lookup + shared token-bucket rate limit (Redis batched reservation, D8) |
| `cache.js` | `/cached` | `OutputCache`-backed response cache, tracks the hit ratio via the `X-Cache` response header |
| `mix.js` | all of the above | weighted random mix (30% bare / 40% cache / 20% jwt / 10% apiKey+rateLimit) |

Numeric performance targets and the profiling pass come next (14.2, 14.3).
