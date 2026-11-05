# Built-in policies

Policies are declared per phase (`inbound`, `backend`, `outbound`, `onError`) in a *scope*: global, gateway, product, API or operation.
A child scope inherits the policies of its parents; use `"inheritance": "override"` to replace an inherited policy of the same type or
`"inheritance": "remove"` to drop it. The list of types and their JSON Schemas is served by `GET /gw/policies`.

Variables usable in header, query and similar values: `{subscription.id}`, `{subscription.product}`, `{subscription.plan}`,
`{claim:<name>}`, `{api.name}`, `{operation.name}`, `{request.method}`, `{request.path}`, `{header:<name>}`, `{var:<name>}`.

| Type | Phase(s) | Purpose |
|---|---|---|
| `headers` | inbound, outbound | Set, append and remove request (`request`) and response (`response`) headers. |
| `cors` | inbound + outbound | Allowed origins, methods, headers, credentials. The gateway answers preflight requests. Declare it in both phases. |
| `ipFilter` | inbound | `allow` / `deny` lists (IP or CIDR). `X-Forwarded-For` is trusted only from `trustedProxies`. |
| `jwt` | inbound | Validates bearer tokens against issuers (`jwksUri` or `metadataAddress`), audiences, `requiredScopes`, `requiredClaims`. Keys are refreshed in the background. |
| `apiKey` | inbound | Resolves a subscription from a key (`header`, optional `queryParameter`). Keys are stored as HMAC-SHA256 hashes (`Gateway:ApiKeyPepper`). |
| `rateLimit` | inbound | `tokenBucket` or `fixedWindow`, per `subscription`, `ip`, `global`, `claim:<n>` or `header:<n>`. Sets `RateLimit-*` and `Retry-After`. Counters are local to each gateway instance. |
| `quota` | inbound | Request budget per `hour`, `day` or `month` (UTC), per subscription. |
| `cache` | inbound | Caches successful `GET` responses (`durationSeconds`, `varyByQuery`, `varyByHeader`, `tags`). Authenticated requests bypass the cache unless `allowAuthenticated` is set. Evict with `POST /gw/cache/evict?tag=`. |
| `timeout` | backend | Bounds the backend call; answers `504`. |
| `retry` | backend | Retries idempotent requests on connection errors and timeouts, with exponential backoff and jitter. Declare it before `timeout`. |
| `circuitBreaker` | backend | Stops calling a failing backend (one breaker per API). Declare it before `retry`. |
| `rewrite` | inbound | Regex path rewrite (relative to the API base path) and query set/remove. |
| `mockResponse` | inbound | Static response without calling the backend. |

Backend policies are ordered outermost first, for example `circuitBreaker`, `retry`, `timeout`.

## Operational endpoints

All require `Authorization: Bearer <Gateway:DiagnosticsToken>` and are disabled when the token is empty:
`GET /gw/config`, `GET /gw/policies`, `POST /gw/cache/evict?tag=<tag>` (automatic tags: `api:<name>`, `operation:<api>/<operation>`).
