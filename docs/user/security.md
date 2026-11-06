# Security

This page holds the threat model of Gatewright (STRIDE), the hardening that ships by default and what you must configure yourself. To report a vulnerability, see [SECURITY.md](../../SECURITY.md).

## Trust boundaries

```
Internet client ──(1)──► Data plane ──(2)──► Backend pods
                            ▲   │
                            │   └──(3)──► Redis
                       (4) gRPC
                            │
Operator / CI ──(5)──► Admin API + Control plane ──(6)──► Database, Kubernetes API
```

| # | Boundary | Authentication |
|---|---|---|
| 1 | Client → data plane | Per API: `jwt`, `apiKey`, `ipFilter` policies; optional TLS termination (SNI) |
| 2 | Data plane → backend | Network policy; optional HTTPS to the backend |
| 3 | Data plane → Redis | Redis password / TLS in the connection string |
| 4 | Data plane ↔ control plane | gRPC service token (`Distribution:Token`) |
| 5 | Operator → Admin API / portal | Entra ID (OIDC) with roles `Admin`, `Publisher`, `Reader` |
| 6 | Control plane → database / Kubernetes | Connection string secret; Kubernetes RBAC (read-only) |

## STRIDE

| Threat | Where | Mitigation in Gatewright | Your responsibility |
|---|---|---|---|
| **S**poofing a client | Boundary 1 | JWT validation (issuer, audience, signature through cached JWKS), API keys stored as peppered HMAC-SHA256 hashes and compared in constant time | Keep `Gateway:ApiKeyPepper` secret; restrict issuers |
| Spoofing a forwarded address | Boundary 1 | `X-Forwarded-*` and `X-Forwarded-For` honored only from `Gateway:ForwardedHeaders:TrustedProxies` | List only your real load balancers |
| Spoofing a data plane | Boundary 4 | Service token required on the gRPC stream | Inject the token from a secret; rotate it; keep the gRPC port off the internet |
| Spoofing an operator | Boundary 5 | Entra ID, role checks on every Admin API endpoint; `DevelopmentBypass` refused outside `Development` | Never enable development auth in production |
| **T**ampering with configuration | Boundaries 5, 6 | Every change is a revision, validated before a snapshot is produced; explicit publication per environment; rollback | Restrict the `Publisher` role |
| Tampering with traffic | Boundaries 1, 2 | TLS termination or upstream TLS; HTTP→HTTPS redirect and HSTS per hostname | Terminate TLS somewhere; use HTTPS to sensitive backends |
| Header injection / request smuggling | Boundary 1 | Kestrel rejects malformed requests; `headers` policy rejects invalid header names and CR/LF/NUL in values at compile time, and strips control characters from values resolved from claims, request headers and variables | Do not forward untrusted values into security-sensitive headers without validating them |
| **R**epudiation | Boundary 5 | Audit trail (who, when, what) on every control plane change; access log with trace id | Ship logs to an append-only store |
| **I**nformation disclosure: secrets | Boundaries 3, 5, 6 | No plaintext secret in configuration or database: references resolved at runtime; certificate material held in memory only, never persisted; API keys shown once | Use Key Vault / Kubernetes Secrets |
| Information disclosure: fingerprinting | Boundary 1 | No `Server` header; operational responses carry `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, a restrictive `Content-Security-Policy` and `Referrer-Policy: no-referrer` | Protect `/gw/*` with `Gateway:DiagnosticsToken`; do not expose `/admin` publicly |
| Information disclosure: cache | Boundary 1 | `cache` policy never shares authenticated responses between users by default | Review `vary` settings |
| **D**enial of service: requests | Boundary 1 | Explicit Kestrel limits (see below); per-policy timeouts; `rateLimit`, `quota`, `circuitBreaker` | Tune limits; rate limit unauthenticated paths |
| Denial of service: control plane | Boundary 4 | Data plane keeps the last known good configuration (D5); the request path never calls the control plane, database or Kubernetes API | Run several data plane replicas |
| Denial of service: Redis | Boundary 3 | Short timeouts, circuit breaker, `failOpen` / `failClosed` per rate limit (D8) | Choose `failClosed` for abuse-sensitive routes |
| **E**levation of privilege | Boundaries 5, 6 | Roles enforced server side; data plane has no Kubernetes rights; control plane reads Services, EndpointSlices and Secrets only; images run as non-root on a chiseled base with a read-only root filesystem | Narrow the `ClusterRole` to namespaced `Role`s when possible |
| Malicious custom policy | In process | Custom policies run in the gateway process (D9): they are trusted code that you build into your own image | Review and sign your own distribution; policies have timeouts but are not sandboxed |

## Request limits

Kestrel limits are explicit and tunable through `Gateway:Limits`:

| Key | Default | Effect when exceeded |
|---|---|---|
| `MaxRequestBodySize` | 30,000,000 bytes | `413 Payload Too Large` |
| `MaxRequestHeadersTotalSize` | 32,768 bytes | `431` |
| `MaxRequestHeaderCount` | 100 | `431` |
| `MaxRequestLineSize` | 8,192 bytes | `414` |
| `RequestHeadersTimeout` | 30 s | connection closed |
| `MinRequestBodyDataRate` | 240 bytes/s (0 disables) | connection closed (slow-loris protection) |

Values that are not positive stop the host at startup. A per-API body limit is not part of V1.

## Security headers

The gateway adds headers only to responses it produces itself (`/admin/*`, `/gw/*`, `/healthz`, `/readyz`). Proxied responses belong to the backend and are never altered by default. To add `Strict-Transport-Security`, `X-Content-Type-Options` or a CSP to a backend, use the `headers` policy; HSTS for a hostname is configured on the hostname itself.

## Supply chain

- CI fails on NuGet vulnerabilities (`TreatWarningsAsErrors`, NU190x) and checks dependency licenses against an allow list.
- Dependabot keeps NuGet packages and GitHub Actions up to date.
- Images are scanned in CI; releases carry an SBOM and provenance attestation (see [release procedure](../release.md)).

## Hardening checklist for production

- [ ] Terminate TLS (Gatewright or upstream) and serve the Admin API/portal only on a private network.
- [ ] Set `Gateway:ApiKeyPepper`, `Distribution:Token` and `Gateway:DiagnosticsToken` from secrets.
- [ ] Configure `Gateway:ForwardedHeaders:TrustedProxies`.
- [ ] Use Entra ID for the Admin API and portal; disable development auth.
- [ ] Apply a Kubernetes `NetworkPolicy` so only the load balancer reaches the data plane and only data planes reach the gRPC port.
- [ ] Review `Gateway:Limits` for your largest legitimate payload.
- [ ] Prefer `failClosed` on rate limits that protect expensive operations.
