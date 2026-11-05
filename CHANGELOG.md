# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial project structure, MIT license and governance files (#0)
- Public policy abstractions: `IGatewayPolicy`, `IPolicyInstance`, `PolicyPhase`, `PolicyResult`, `GatewayContext` (#1.1)
- JSON configuration model: gateways, products, APIs, operations, backends and policy sets (#1.2)
- Policy scope inheritance (global, gateway, product, API, operation) with `inherit`, `override` and `remove` operators (#1.3)
- Policy registry with JSON Schema validation and detailed configuration errors (#1.4)
- Policy pipeline with short-circuit, `OnError` phase, per-policy timeouts, tracing and duration metrics (`Gatewright.Policies` meter) (#1.5)
- Configuration validator reporting all errors (duplicate names, path conflicts, unknown policies, backends and products) (#1.6)
- Single host with `Gateway:Mode` (`All`, `ControlPlane`, `DataPlane`); invalid mode or missing config file fails at startup (#2.1)
- Data plane applies configuration snapshots atomically and keeps the last known good one when YARP rejects a new one (#2.3)
- Policies run around proxied calls: Inbound rejection, Outbound response headers, OnError (#2.4)
- JSON file configuration source (`Gateway:ConfigFile`, `Gateway:ConfigPollInterval`) with hot reload and in-process snapshot distribution in mode `All` (#2.5)
- `/healthz`, `/readyz` (ready once a valid configuration is applied) and token-protected `/gw/config` (`Gateway:DiagnosticsToken`) (#2.6)
- Local development sample (`samples/dev-config.json`, `samples/backend-orders`) and getting-started guide (#2.7)
- `headers` policy: set, append and remove request/response headers, with variables like `{subscription.id}` and `{claim:tid}` (#3.1)
- `cors` policy: allowed origins, methods, headers, credentials; preflight answered by the gateway (#3.2)
- `ipFilter` policy: allow/deny IP and CIDR lists; `X-Forwarded-For` honored only behind configured trusted proxies (#3.3)
- JWT policy with background JWKS refresh, multiple issuers, required claims and scopes; identity exposed to later policies (#3.4)
- `apiKey` policy and subscriptions: keys are stored as HMAC-SHA256 hashes (`Gateway:ApiKeyPepper`), resolved from a local in-memory store (#3.5)
- `rateLimit` policy: token bucket or fixed window per subscription, IP, claim or header, with `RateLimit-*` and `Retry-After` headers (local counters) (#3.6)
- `quota` policy: per-subscription request budget per hour, day or month (in-memory counters; `IQuotaStore` ready for a shared store) (#3.7)
- `cache` policy: per-operation response cache varying by query and headers, never shared between authenticated users by default; tag invalidation through `POST /gw/cache/evict?tag=` (#3.8)
- `timeout`, `retry` (idempotent requests, exponential backoff with jitter) and `circuitBreaker` (per cluster) policies around the backend call (#3.9)
- `rewrite` policy: regex path rewrite relative to the API base path and query-string set/remove (#3.10)
- `mockResponse` policy: static response without calling the backend (#3.11)
- Policy catalog endpoint `GET /gw/policies` listing policy types, phases and JSON Schemas (#3.12)
- Control plane domain model, revisions with publication to environments, rollback and diff (#4.1, #4.3)
- `ControlPlane:Provider` (`Sqlite`, default; or `Postgres`): PostgreSQL schema created and upgraded with versioned EF Core migrations (#4.2)
- Admin API `/admin/v1`: CRUD for APIs, operations, products, plans, consumers, subscriptions and shared policies, publication, rollback, draft validation (`POST /validate`), OpenAPI document (#4.4)
- Declarative export/import of a whole environment (`GET /admin/v1/export`, `POST /admin/v1/import`, `?dryRun=true`) for GitOps and promotion between environments; export then import yields the same configuration hash (#4.8)
- `rateLimit` option `shared: true`: gateway instances share one budget by reserving batches of tokens from a store (`onStoreFailure`: `failOpen` or `failClosed`) (#5.1)
- Redis shared state (`Redis:ConnectionString`): batched global rate limits, strict quota counters and key revocation pub/sub; short timeouts, circuit breaker and `gatewright.redis.*` metrics (#5.2, #5.3, #5.5)
- Key revocations (revoke, rotate, deactivate, delete) reach all gateways within seconds through the revocation feed (Redis pub/sub when configured) (#5.4)
- Control plane / data plane split over gRPC: snapshots and deltas with debounce, service-token authentication, reconnection with backoff, last-known-good on control plane loss, `GET /admin/v1/nodes` and the `gatewright.config.version_applied` metric (#6.1-#6.5)
- Kubernetes discovery: Services annotated `gatewright.io/expose`, ready EndpointSlice addresses pushed to data planes without a new publication, Lease-based leader election, passive health checks for service backends, `GET /admin/v1/discovery/services` (#7.1, #7.2, #7.3, #7.6)
- `backend.healthCheck` option (active and passive) for any backend (#7.6)
- TLS termination with SNI certificate selection and hot reload, per-hostname HTTP→HTTPS redirect and HSTS, `hostnames` in the configuration and Admin API (#8.1, #8.2)
- Trusted-proxy handling of `X-Forwarded-*` headers (`Gateway:ForwardedHeaders:TrustedProxies`) (#8.3)
- WebSocket and gRPC (HTTP/2) passthrough; `backend.httpVersion: "2"` forces HTTP/2 to the backend (#8.4)
- Graceful shutdown: `/readyz` fails as soon as shutdown starts, `Gateway:Shutdown:DrainDelaySeconds` (#8.5)
- C# SDK: `GatewayPolicy<TConfig>` with JSON Schema generated from the configuration type, `GatewayHost.CreateBuilder(args).AddPolicy<T>()`, `Gatewright.Testing` (`GatewayTestHost`, `SimulatedBackend`) and the `samples/custom-policy` distribution (#9.1, #9.2, #9.3, #9.5, #9.7)
- `dotnet new gatewright-host` template; NuGet packages versioned from git tags (MinVer) with README, license and third-party notices (#9.4, #9.6)
- `x-gateway` OpenAPI extension (version 1) and OpenAPI 3.0/3.1 import (JSON or YAML) with an import report, `POST /admin/v1/apis/import` (#10.1, #10.2)
- Automatic OpenAPI import of Kubernetes services annotated `gatewright.io/openapi-path`, optional auto-publication per environment (`Kubernetes:AutoImport:PublishTo`) (#7.4)
- Hostnames can reference a Kubernetes TLS secret by name (`"certificate": { "secret": "namespace/name" }`) instead of a mounted file: the control plane reads it (`Kubernetes:Secrets:PollSeconds`) and pushes the certificate and key to every data plane over the distribution channel, held in memory only, never persisted (#7.5)
- `Gatewright.Exposure` for ASP.NET Core backends: `WithRateLimit`, `CacheOnGateway`, `WithGatewayPolicy`, MVC attributes and `AddOpenApi(o => o.AddGatewayExposure())` generate `x-gateway` in the native OpenAPI document (#10.3)
- `gatewright` CLI (.NET tool): `login`, `api import`, `config export/import/validate`, `publish`, `rollback`, `nodes` (#10.4)
- Web portal (Blazor): dashboard, API catalog with effective policies, OpenAPI import, schema-driven policy editor, products and subscriptions with one-time keys, revisions with diff/publish/rollback, audit, test console with per-policy trace (#11.1-#11.8)
- Request tracing: send `x-gatewright-trace: <Gateway:DiagnosticsToken>` to receive `x-gatewright-trace-result` (per-policy duration and result) (#11.7)
- `GET /admin/v1/policy-types` (#11.4)
- Admin API security with Entra ID (or any OIDC) JWT bearer tokens, `Admin`/`Publisher`/`Reader` roles and an audit trail (`GET /admin/v1/audit`) (#4.5)
- Subscription keys: generation (shown once), primary/secondary rotation and revocation; only HMAC hashes are stored (#4.6)
- `Gateway:ConfigSource=Database`: single-process mode serving the active publication of `Gateway:Environment` (#4.4)
- OpenTelemetry traces, metrics and logs with OTLP export (`Telemetry:Otlp:Endpoint`) (#12.1)
- Business metrics `gatewright.requests`, `gatewright.request.duration` and `gatewright.rejections` by API, operation, product and cause (#12.2)
- Asynchronous, sampled access logs with masking of keys and tokens (#12.3)
- Usage analytics: local aggregation, batched `IUsageSink` and OTLP sink (#12.4)
- Grafana dashboard, Azure Monitor queries and Prometheus alert rules in `deploy/observability` (#12.5)
- Root `Dockerfile` and eight ready-to-run samples (`samples/01..08`, including a product catalogue with subscriptions and API keys, and an otel-gui viewer) with Docker/Podman/Colima compose files: quickstart, control plane + data planes + portal, Redis shared rate limit, custom policy, observability, no container (#S.1)
- `Telemetry:Otlp:Protocol` (`grpc` or `http/protobuf`) to export OTLP straight to HTTP-only backends such as otel-gui (#S.1)
- Official `gatewright-host` and `gatewright-portal` container images (`dotnet publish -t:PublishContainer`): non-root, chiseled base, OCI license/source/version labels; CI scans both with Trivy on every push and pull request (#13.1)
- Helm chart (`deploy/helm/gatewright`): `gateway.mode` selects a single `all` Deployment or independent `controlPlane`/`dataPlane` Deployments; optional web portal; `postgres.connectionString` switches the control plane to PostgreSQL; overridable `image.repository` for customer distribution (#13.2)
- Helm chart: data plane `HorizontalPodAutoscaler` (CPU, optional RPS custom metric), `PodDisruptionBudget`, `topologySpreadConstraints`, and default CPU/memory requests and limits for every component (#13.3)
- Helm chart: RBAC scoped to what the control plane actually needs (read Services/EndpointSlices/Secrets, a leader-election Lease), `NetworkPolicy` per component, optional Azure Workload Identity labeling (#13.4)
- Helm chart: `redis.dev.enabled`/`postgres.dev.enabled` bundle a single dev-only Redis/PostgreSQL in the same release, for trying things out without an external Redis or PostgreSQL (#13.5)
- `scripts/k8s-e2e.sh` and CI workflow: end-to-end test of the Helm chart on a real Kubernetes cluster — install, Kubernetes service discovery, Admin API, publish, proxy round trip (#13.6)
- `GET /gw/lifecycle/prestop` and Helm chart value `gateway.shutdown.drainDelaySeconds` (default 5s): the data plane and `all`-mode Deployments now wire this as a Kubernetes `preStop` hook, so a terminating pod keeps serving, already marked not-ready, until the Service's Endpoints update has had time to reach every node before the process actually stops (#14.2)

### Changed

- The web portal now has a browser tab icon (SVG, with an `.ico` fallback) (#S.8)
- New look for the web portal: top bar with tabs, cards, status pills, a policy-trace waterfall in the test console and consistent forms on every page (#S.8)
- Idle database load of the control plane reduced from about 3 queries per second to 1 (a one-value query; the published snapshot is read only when it changes), and EF Core SQL commands are no longer logged by default (set `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Information` to see them); poll rate is `Gateway:ConfigPollInterval` (#S.6)
- The default environments are now `dev`, `staging` and `prod` (the second one was named `recette`); an existing development database keeps its old `recette` environment next to the new one (#S.5)

### Deprecated

### Removed

### Fixed

- `Gateway:ConfigPollInterval` outside 0 to 1 day (for example `30000`, which .NET reads as days) now fails at startup with a message explaining the `hh:mm:ss` format, instead of crashing the control plane with a timer error (#S.7)
- Data plane pods could drop requests during a scale-down or a rolling update: without a `preStop` hook, Kubernetes sent `SIGTERM` the instant a pod was marked for deletion, before the Service's Endpoints update had reached every node, so some in-flight connections landed on a pod already gone from the Endpoints list. Fixed by the `preStop` hook above (#14.2)

### Security

[Unreleased]: https://github.com/pgourlain/gatewright/commits/main
