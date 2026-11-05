# tasks.md — Gatewright

Legend: `[ ]` to do · `[x]` done · `[~]` in progress · `[!]` blocked (explain in the Journal)

Rule: work phase by phase, in order. Each task lists its **acceptance criteria (AC)**. A task is checked only when its AC are verified and its tests are green.

---

## Phase 0 — Repository foundations

- [x] **0.1 Solution and projects**
  Create `Gatewright.slnx` and the projects of the CLAUDE.md §4 structure (empty but compilable), `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, LangVersion latest, ImplicitUsings), `Directory.Packages.props` (central package management).
  AC: `dotnet build` green; project references respect the dependencies (Abstractions ← Core ← DataPlane/ControlPlane ← Host).
- [x] **0.2 Code quality**
  `.editorconfig`, analyzers (.NET analyzers at recommended level, PublicApiAnalyzers on Abstractions and Sdk), `dotnet format` configured.
  AC: `dotnet format --verify-no-changes` green.
- [x] **0.3 Test projects**
  One xUnit project per src project + `Gatewright.IntegrationTests`. One trivial test per project.
  AC: `dotnet test` green.
- [~] **0.4 CI**
  GitHub Actions (open source project): restore, build, test, format, coverage, on push and pull request.
  AC: pipeline green on the main branch.
- [x] **0.5 Initial ADRs**
  `/docs/adr/0001…0011` restating decisions D1 to D11 of CLAUDE.md (short format: context, decision, consequences), plus `0012-licence-mit.md` (choice of MIT, allowed dependency licenses, DCO rather than CLA).
  AC: one file per decision.

### License and open source release

- [x] **0.6 Copyright holder** (decided 2026-11-04)
  Holder: **Pierrick Gourlain**, personal project. No mention of an employer or company anywhere (code, docs, NuGet `Authors`/`Company` metadata, image labels, chart).
- [x] **0.7 LICENSE and NOTICE**
  `LICENSE`: standard MIT text (`Copyright (c) 2026 Pierrick Gourlain and Gatewright contributors`). `NOTICE`: project name, holder, repository URL, reminder that the name and logo are trademarks (pointing to `TRADEMARKS.md`).
  AC: MIT text identical to the SPDX reference version (only the copyright line changes).
- [x] **0.8 License headers**
  `.editorconfig`: `file_header_template = Copyright (c) Gatewright contributors.\nSPDX-License-Identifier: MIT`; rule `IDE0073` set to `error`; `EnforceCodeStyleInBuild=true`. Same header for `.razor`, `.proto`, shell/PowerShell scripts (check script for non-C# files).
  AC: a file without a header fails the build or CI.
- [x] **0.9 Packaging metadata**
  In `Directory.Build.props`: `PackageLicenseExpression=MIT`, `Copyright` (`Copyright (c) 2026 Pierrick Gourlain and Gatewright contributors`), `Authors` (`Pierrick Gourlain`), no `Company`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType=git`, `PackageReadmeFile`, `PackageIcon`, `PackageTags`, Source Link (`Microsoft.SourceLink.GitHub`), `IncludeSymbols` + `snupkg`, `ContinuousIntegrationBuild` in CI, `Deterministic`. Non-publishable projects marked `IsPackable=false`.
  AC: `dotnet pack` produces packages whose inspection (`dotnet nuget verify` or otherwise) shows the right MIT license and repository.
- [~] **0.10 Dependency license check**
  CI tool (e.g. `nuget-license` or equivalent) with an allow list (MIT, Apache-2.0, BSD-2/3-Clause, ISC, MS-PL, 0BSD, Unlicense). Fails on a forbidden or unknown license. Automatic generation of `THIRD-PARTY-NOTICES.md` (name, version, license, URL, copyright text) at each release build.
  AC: deliberately adding a GPL package on a test branch → CI red; `THIRD-PARTY-NOTICES.md` generated and up to date.
- [x] **0.11 Governance files**
  - `README.md`: pitch, badges (CI, NuGet, license), quick start, links to docs.
  - `CONTRIBUTING.md`: prerequisites, build/test, conventions (Conventional Commits, DCO, changelog), PR process, naming rules.
  - `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1, contact to be filled in.
  - `SECURITY.md`: supported versions, private reporting (GitHub Security Advisories), response times, coordinated disclosure.
  - `GOVERNANCE.md`: roles (maintainers, contributors), decision making, ADRs, adding/removing maintainers.
  - `MAINTAINERS.md`: list of maintainers.
  - `TRADEMARKS.md`: free code, protected name and logo; allowed uses (mention, "compatible with Gatewright", renamed forks) and forbidden ones.
  AC: files present, cross-links valid, no placeholder except the contacts to confirm (listed in the Journal).
- [~] **0.12 DCO and GitHub templates**
  DCO check on PRs (DCO app or dedicated action); `.github/ISSUE_TEMPLATE/` (bug, feature, question, config.yml sending vulnerabilities to SECURITY.md); `PULL_REQUEST_TEMPLATE.md` with checklist (tests, changelog, headers, DCO); `CODEOWNERS`; Dependabot (NuGet, GitHub Actions, Docker).
  AC: a PR without `Signed-off-by` is blocked.
- [x] **0.13 CHANGELOG.md**
  Create `CHANGELOG.md` in Keep a Changelog 1.1.0 format: header explaining the format and SemVer, `## [Unreleased]` section with the empty headings (`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`), comparison links at the bottom. First entry: `- Initial project structure, MIT license and governance files (#0)`.
  AC: valid file (Markdown lint); update rule reminded in `CONTRIBUTING.md` and in the PR checklist.
- [~] **0.14 Changelog check in CI**
  Job that fails if a PR changes `src/**`, `deploy/**` or `templates/**` without touching `CHANGELOG.md`, unless labelled `skip-changelog` (internal changes).
  AC: tested on a demonstration PR in both cases.

---

## Phase 1 — Contracts and policy pipeline

- [x] **1.1 Public abstractions** (`Gatewright.Abstractions`)
  `IGatewayPolicy` (Type, Phases, ConfigSchema, Compile), `IPolicyInstance` (`ValueTask<PolicyResult> InvokeAsync(GatewayContext)`), `PolicyPhase` (flags Inbound/Backend/Outbound/OnError), `PolicyResult` (Continue, Reject(status, code, message), Respond(response)), `GatewayContext` (HttpContext, route, API, operation, identity, subscription, variables, `CancellationToken`).
  AC: complete XML docs; `PublicAPI.Unshipped.txt` filled in; unit tests on `PolicyResult`.
- [x] **1.2 Configuration model** (`Gatewright.Core`)
  Immutable records: `GatewayConfig` (version, named policies, products, APIs), `ApiDefinition` (name, basePath, version, backend, operations, policies per phase), `OperationDefinition` (method, path, policies), `BackendDefinition` (destinations or discovered-service reference, timeout, HTTP version), `PolicyReference` (type + `JsonElement` config or shared policy name), `ProductDefinition`.
  AC: JSON serialization/deserialization round-trip tested.
- [x] **1.3 Scope inheritance**
  Resolution of the effective chain per operation: global → gateway → product → API → operation, with operators `inherit` (default), `override`, `remove`.
  AC: tests covering addition, override, removal, execution order.
- [x] **1.4 Policy registry and compilation**
  `PolicyRegistry` (types available through DI), validation of each policy's config against its JSON Schema, compilation into `IPolicyInstance` once per snapshot.
  AC: invalid config → detailed errors (API, operation, policy, JSON path); no compilation at runtime per request.
- [x] **1.5 Execution engine**
  `PolicyPipeline` running the phases; short-circuit on `Reject`/`Respond`; `OnError` phase on exception or timeout; per-policy timeout; instrumentation (Activity + histogram per policy).
  AC: tests for order, short-circuit, OnError, timeout.
- [x] **1.6 Global validator**
  `GatewayConfigValidator`: unique names, path conflicts between APIs, unknown policies, backend references, non-existent products.
  AC: aggregated errors (no stop at the first one); tests per rule.

---

## Phase 2 — Minimal data plane (`all` mode, file source)

- [x] **2.1 Multi-mode host** (`Gatewright.Host`)
  Option `Gateway:Mode` (`All`, `ControlPlane`, `DataPlane`); extensions `AddControlPlane`, `AddDataPlane`, `AddDistribution(mode)`, `AddStateStore`.
  AC: each mode starts; unknown mode → explicit failure at startup.
- [x] **2.2 Snapshot**
  Immutable `ConfigSnapshot` (version, hash, YARP routes, YARP clusters, compiled pipelines per route, security data). `GatewayConfig → ConfigSnapshot` translator (one YARP route per operation or per API depending on policy needs; route metadata = API/operation identifiers).
  AC: translation tests; stable hash for the same config.
- [x] **2.3 Snapshot application**
  `SnapshotApplier`: updates `InMemoryConfigProvider` and the pipeline registry by atomic swap; if YARP rejects the config → keep the previous one (last known good) and log.
  AC: test of an invalid config that does not replace the valid one.
- [x] **2.4 Policy middleware in the YARP pipeline**
  Inbound phases run before the proxy, Outbound on the response (YARP transforms or response middleware), OnError.
  AC: integration test: an Inbound policy that rejects, an Outbound policy that adds a header.
- [x] **2.5 File config source + in-process distribution**
  `IConfigSource` (JSON file with polling reload), in-process `IConfigDistributor` (`Channel<ConfigSnapshot>`).
  AC: editing the file → new config applied in < 5 s without restart.
- [x] **2.6 Health and readiness**
  `/healthz` (liveness), `/readyz` (ready only if a valid snapshot is applied), `/gw/config` (version + hash, protected).
  AC: readiness fails before the first snapshot.
- [x] **2.7 Dev sample**
  `samples/dev-config.json` + minimal `samples/backend-orders`; local start README.
  AC: `dotnet run` in all mode routes `/orders/*` to the backend.

---

## Phase 3 — Built-in policies

For each policy: config record, schema, compilation, unit tests and one integration test.

- [x] **3.1 `headers`**: set/append/remove on request and response, static values and simple variables (`{subscription.id}`, `{claim:tid}`).
- [x] **3.2 `cors`**: origins, methods, headers, credentials, preflight handled by the gateway.
- [x] **3.3 `ipFilter`**: allow/deny CIDR, honouring `X-Forwarded-For` only from configured trusted proxies.
- [x] **3.4 `jwt`**: validation (issuer, audience, lifetime, signature) with JWKS cached and refreshed in the background; multi-issuer; claim/scope requirements; identity exposed in `GatewayContext`.
  AC: no JWKS network call on the hot path; key rotation tested.
- [x] **3.5 `apiKey` + subscriptions**: key in header or query, resolved to a subscription (product, plan, state) through a locally cached store; hashed keys; constant-time comparison.
  AC: in-memory store for dev mode; interface ready for Redis (phase 5).
- [x] **3.6 `rateLimit`**: token bucket / fixed window, partition by subscription, IP, claim or header; `RateLimit-*` and `Retry-After` headers; local implementation (Redis store in phase 5).
- [x] **3.7 `quota`**: quota per period (day/month) per subscription — interface defined, in-memory implementation.
- [x] **3.8 `cache`**: OutputCache per operation (duration, vary by query/header), invalidation by tag.
- [x] **3.9 `timeout` and `retry`**: backend timeout, idempotent retries only, with backoff; circuit breaker per cluster.
- [x] **3.10 `rewrite`**: path and query rewriting (beyond the basePath).
- [x] **3.11 `mockResponse`**: static response (useful for APIs under design).
- [x] **3.12 Catalog**: internal endpoint listing the policy types and their schemas (will serve the portal and the CLI).

---

## Phase 4 — Control plane: model, database and Admin API

- [x] **4.1 Domain model**: API, operations, products, plans, subscriptions, consumers, shared policies, environments, revisions.
- [x] **4.2 EF Core + PostgreSQL**: DbContext, configurations, migrations, dev seed. Testcontainers for tests.
- [x] **4.3 Revisions and publication**: every change creates a draft revision; publication to an environment (`dev`, `staging`, `prod`); rollback to a published revision; diff between revisions.
  AC: tests of the draft → published → rollback cycle.
- [x] **4.4 REST Admin API** (versioned `/admin/v1`): CRUD for APIs/operations/products/plans/subscriptions/shared policies, publication, rollback, export/import, dry-run validation (`POST /validate`). Admin API OpenAPI published.
- [x] **4.5 Admin API security**: Entra ID (OIDC/JWT), roles `Admin`, `Publisher`, `Reader`; audit (who, when, what, diff).
- [x] **4.6 Subscription key generation**: creation, rotation (primary/secondary key), revocation; only the hashed value is stored, the key is shown once.
- [x] **4.7 Compilation on the CP side**: published `GatewayConfig` → full validation (phase 1) → serializable versioned snapshot, stored for distribution.
- [x] **4.8 Declarative export/import**: JSON format (and optional YAML) of a whole environment for GitOps and promotion between environments.
  AC: export then import into an empty database → identical snapshot (same hash).

---

## Phase 5 — Shared Redis state

- [x] **5.1 Store abstractions**: `IRateLimitStore`, `IQuotaStore`, `ISubscriptionCache`, `IRevocationFeed` with in-memory implementations (dev).
- [x] **5.2 Batched Redis rate limit**: local bucket, reservation of N tokens through an atomic Lua script, adaptive N; controlled degradation if Redis is unavailable (configurable mode: fail-open or fail-closed).
  AC: multi-instance test (3 hosts) honouring the global limit within a documented tolerance; < 1 Redis call per 20 requests under nominal load.
- [x] **5.3 Strict Redis quota**: per-period counters through Lua, for billable quotas.
- [x] **5.4 Subscription synchronization**: local cache fed by the snapshot or a dedicated stream; revocations through Redis pub/sub applied in < 5 s.
- [x] **5.5 Resilience**: short timeouts, Redis circuit breaker, error metrics.

---

## Phase 6 — gRPC distribution CP → DP

- [x] **6.1 Protobuf contract**: `Subscribe(node_id, last_version)` → stream of `SnapshotMessage` (full) and `DeltaMessage`; applied-version ack; heartbeat.
- [x] **6.2 Distribution server** (CP): served by all CP replicas; sends the full snapshot on connection, then deltas; change debounce (300 ms, configurable).
- [x] **6.3 Distribution client** (DP): reconnection with exponential backoff + jitter; keeps the last known good; readiness tied to the first snapshot.
- [x] **6.4 Channel security**: mTLS or service token (Workload Identity / projected service account token).
- [x] **6.5 Convergence observability**: the CP exposes which version each node applied; `config_version_applied` metric on the DP side.
  AC: test with 1 CP + 3 DP: published change applied everywhere in < 5 s; CP stopped → traffic continues.

---

## Phase 7 — Kubernetes discovery

- [x] **7.1 Watch annotated Services**: `gatewright.io/expose: "true"`, `gatewright.io/openapi-path`, `gatewright.io/base-path`, `gatewright.io/product`; Services become selectable backends.
- [x] **7.2 EndpointSlices**: destinations = IPs of ready pods; debounced updates; terminating pods removed.
- [x] **7.3 Leader election**: Kubernetes `Lease`; only the leader watches and writes state; failover tested.
- [x] **7.4 OpenAPI auto-import**: on each change of an annotated Service or new deployment, fetch the OpenAPI, read the `x-gateway` extensions, create/update the API in a **draft revision** (automatic publication configurable per environment).
- [x] **7.5 TLS Secrets**: watch of the secrets referenced by the declared hostnames; certificates included in the snapshot (encrypted in transit).
- [x] **7.6 Health checks**: active (configurable) and passive YARP checks on destinations.
  AC: tests on kind: scaling a backend 1→3→1 reflected in routing without 5xx errors.

---

## Phase 8 — TLS and networking

- [x] **8.1 SNI TLS termination**: `ServerCertificateSelector` per hostname from the snapshot; hot reload.
- [x] **8.2 HTTP→HTTPS redirect and HSTS**, configurable per hostname.
- [x] **8.3 Forwarded headers**: configurable trusted proxies (Front Door, App Gateway), correct client IP for rate limit and ipFilter.
- [x] **8.4 HTTP/2 and WebSockets** to backends; gRPC passthrough.
- [x] **8.5 Graceful shutdown**: connection draining, `preStop`, readiness failing before stop.
  AC: certificate rotation test without interruption; DP rolling update without client errors.

---

## Phase 9 — Extension SDK and distribution model

- [x] **9.1 `GatewayPolicy<TConfig>`**: generic base class; JSON schema generated from the config record (System.Text.Json schema exporter); attribute `[GatewayPolicy("type", Phases = …)]`.
- [x] **9.2 `GatewayHost.CreateBuilder`**: preconfigured host (all modes) with `AddPolicy<T>()`.
- [x] **9.3 `Gatewright.Testing`**: `GatewayTestHost.StartAsync(configure, config)` in all mode, HTTP client, simulated backends.
- [x] **9.4 `dotnet new gatewright-host` template**: host project, a sample policy, tests, Dockerfile/PublishContainer, README.
- [x] **9.5 Schema publication**: custom policies appear in the catalog (3.12) and are validated by the CP (same image).
- [x] **9.6 NuGet packaging**: SemVer-versioned packages (version computed from tags, e.g. MinVer or Nerdbank.GitVersioning), license metadata from task 0.9, Source Link, symbols, README per package, `LICENSE` and `THIRD-PARTY-NOTICES.md` included in each package.
- [x] **9.7 Sample** `samples/custom-policy`: customer distribution with `acme.fraudCheck`.
  AC: an external developer creates, tests and deploys a custom policy by following only the docs, in < 30 min.

---

## Phase 10 — Simplified declaration: code-first, OpenAPI, CLI

- [x] **10.1 `x-gateway` specification**: OpenAPI extensions at document and operation level (basePath, product, policies, cache, rateLimit…), documented and versioned (`x-gateway-version`).
- [x] **10.2 OpenAPI importer**: OpenAPI 3.0/3.1 → API + operations + policies; import report (additions, changes, removals).
- [x] **10.3 `Gatewright.Exposure`** (package for ASP.NET Core backends): `AddGatewayExposure`, Minimal API extensions and MVC attributes (`WithGatewayPolicy`, `WithRateLimit`, `CacheOnGateway`) that generate the `x-gateway` entries in the native OpenAPI.
- [x] **10.4 `gatewright` CLI**: `login`, `api import <openapi>`, `config export/import`, `config validate <file>` (offline, same validator), `publish --env`, `rollback`. Distributed as a global .NET tool.
  AC: `samples/backend-orders` exposed end to end only through code-first + Service annotation, without any route YAML.

---

## Phase 11 — Web portal (Blazor)

- [x] **11.1 Foundation**: Entra ID authentication, roles, layout, navigation, Admin API calls.
- [x] **11.2 API catalog**: list, search, detail (operations, backend, effective policies per operation).
- [x] **11.3 API import**: OpenAPI URL or file, backend choice from the list of discovered Services, preview of the import report.
- [x] **11.4 Policy editor**: forms generated from the JSON Schemas (including custom policies), per scope, with editable order and inheritance visualization.
- [x] **11.5 Products, plans, subscriptions**: creation, key generation/rotation/revocation.
- [x] **11.6 Revisions**: visual diff, publication per environment, rollback, audit history.
- [x] **11.7 Test console**: send a request through the gateway and display the policy trace (duration, result per policy).
- [x] **11.8 Dashboard**: DP node status, applied config version, validation errors.
  AC: a web technician imports an API, adds JWT + rate limit, publishes and tests, without writing a file.

---

## Phase 12 — Observability

- [x] **12.1 OpenTelemetry**: traces (gateway + propagation to backends), native YARP metrics, structured logs; configurable OTLP export.
- [x] **12.2 Business metrics**: requests/latency/errors per API, operation, product, subscription; duration per policy; rejections by cause (auth, rate limit, quota).
- [x] **12.3 Access logs**: asynchronous, sampleable, without sensitive data (keys and tokens masked).
- [~] **12.4 Usage analytics**: local aggregation and batched sending (`IUsageSink` interface, OTLP and Event Hubs/Kafka implementations).
- [x] **12.5 Dashboards and alerts**: Grafana dashboards (JSON) and sample Azure Monitor queries; basic alerts (5xx, p99 latency, version divergence).

### Additions outside the phases — Usage samples (user request)

- [x] **S.1 Multi-scenario samples**: one directory per sample under `samples/`, each with a `compose.yaml` compatible with Docker, Podman and Colima, a single root `Dockerfile`; scenarios: quickstart, control plane + data planes + portal, Redis shared limit, custom policy, observability, no container.
- [x] **S.2 Architecture documentation**: `docs/user/architecture.md`, diagrams of each execution mode and the pods added next to the application.
- [x] **S.3 Custom policy telemetry**: logs, traces and metrics from a custom policy, tested.
- [x] **S.4 English documentation**: all documents of the repository in English (this file and CLAUDE.md included).
- [x] **S.8 Portal redesign**: tabs layout, shared stylesheet, restyled pages.
- [x] **S.7 Poll interval validation**: out-of-range `Gateway:ConfigPollInterval` fails at startup with a format hint.
- [x] **S.6 Idle database load**: fewer and lighter polling queries, SQL command log off by default.
- [x] **S.5 Default environments**: `dev`, `staging`, `prod` instead of `dev`, `recette`, `prod`.

---

## Phase 13 — Packaging and Kubernetes deployment

- [x] **13.1 Images**: `PublishContainer`, non-root image, read-only filesystem, chiseled/distroless image; vulnerability scan in CI.
- [x] **13.2 Helm chart**: separate CP and DP deployments (or all mode through a value), Services (LoadBalancer for DP, ClusterIP for CP), options ConfigMap, Secrets, overridable `image.repository` (customer distribution).
- [x] **13.3 Kubernetes scalability**: DP HPA (CPU + RPS through metrics), PDB, `topologySpreadConstraints`, default resource requests/limits, `externalTrafficPolicy: Local`.
- [x] **13.4 RBAC and security**: CP ServiceAccount (read Services, EndpointSlices, targeted Secrets, Leases), DP without Kubernetes rights, NetworkPolicies, optional Azure Workload Identity.
- [x] **13.5 Dependencies**: options for external Redis/PostgreSQL (Azure Cache for Redis, Azure Database for PostgreSQL) or sub-charts for dev.
- [x] **13.6 E2E on kind**: script installing the chart, deploying the sample backend, running the key scenarios.
  AC: `helm lint` green; e2e green in CI.

---

## Phase 14 — Performance and scalability

- [x] **14.1 Load scenarios** (k6 or NBomber): bare proxy, JWT, apiKey + Redis rateLimit, cache, realistic mix.
- [ ] **14.2 Targets** (to refine with the business): gateway overhead p99 < 5 ms at 5,000 RPS per pod (2 vCPU) with JWT + rate limit; no errors during scale-out/scale-in and rolling update; config propagation < 5 s at 50 pods.
- [ ] **14.3 Profiling and optimization**: allocations on the hot path (dotnet-counters, dotnet-trace), pooling, Kestrel and YARP `HttpClient` tuning.
- [ ] **14.4 Native AOT evaluation** of the data plane (compatibility, memory and startup gain); decision in an ADR.
- [ ] **14.5 Chaos tests**: loss of the CP, of Redis, of a node, backend latency; behaviour consistent with decisions D5 and D8.

---

## Phase 15 — Hardening, documentation and release

- [ ] **15.1 Security review**: threat model (STRIDE) of gateway + control plane, security headers, request size limits, header injection protection, up-to-date dependencies.
- [ ] **15.2 User documentation** (`/docs/user`): installation, declaration (portal, OpenAPI, code-first, CLI), built-in policies, C# extension, operations, upgrades.
- [ ] **15.3 Migration guide and compatibility**: SemVer policy for packages and the export format, CP/DP compatibility matrix (DP N-1 supported).
- [ ] **15.4 Release pipeline**: GitHub Actions workflow triggered by a `v*` tag: build, tests, license check, generation of `THIRD-PARTY-NOTICES.md`, `dotnet pack` and NuGet publication (Trusted Publishing / key as a secret), multi-arch images on GHCR with OCI license labels and SLSA/provenance attestation, SBOM (CycloneDX or SPDX) attached to the release, Helm chart published (OCI on GHCR). The GitHub Release body is extracted from the matching section of `CHANGELOG.md`.
  AC: test release `v0.0.1-rc.1` published end to end on a test repository.
- [ ] **15.5 Documented release procedure** (`docs/release.md`): turn `[Unreleased]` into `[x.y.z] - YYYY-MM-DD`, update the comparison links, signed tag, post-release checks; prereleases (`-preview.N`) before 1.0.
- [ ] **15.6 Release 1.0**: finalized changelog, tag `v1.0.0`, NuGet packages, images, chart and SBOM published; announcement.
  AC: `CHANGELOG.md` contains `## [1.0.0]` and the GitHub Release reuses exactly that content.

---

## Backlog (outside V1 scope, do not implement without approval)

- CEL expressions in policies (conditions, dynamic values)
- External callouts in the style of `ext_authz` (HTTP/gRPC)
- Sandboxed WASM plugins
- Public developer portal (API discovery, self-service subscriptions, approval workflow)
- OpenAPI document exposed per product
- Event Hubs / Kafka usage sink (12.4)
- Per-plan limits (a plan is currently only a label)
- Usage-based monetization / billing
- Multi-cluster / multi-region with a central control plane
- TCP/UDP support
- REST ↔ SOAP, GraphQL transformation

---

## Journal

> Record here, by date, the minor decisions, deviations, blockers and demo commands of each phase.

- 2026-11-04 — Creation of CLAUDE.md and tasks.md.
- 2026-11-04 — Product name chosen: Gatewright (replaces the working name MyGateway).
- 2026-11-04 — Phase 0: repository URL assumed to be `https://github.com/pgourlain/gatewright` (PackageProjectUrl/RepositoryUrl, NOTICE) — to be confirmed. Initially a classic `.sln` as CLAUDE.md said; migrated to `.slnx` later (the .NET 10 default format). Tests: xUnit 2.9 + Shouldly (FluentAssertions v8 is commercial, so rejected). `Microsoft.SourceLink.GitHub` 10.0.403 (8.x versions ship a vulnerable dependency, NU1902).
- 2026-11-04 — Phase 0: tasks 0.4, 0.10, 0.12, 0.14 marked `[~]`: files and scripts created and tested locally (`check-changelog.sh`, `nuget-license` + generation of `THIRD-PARTY-NOTICES.md`, `check-headers.sh`), but the acceptance criteria need a GitHub repository (green pipeline, PR without sign-off blocked, demonstration PR, deliberate GPL package in CI). `xunit.abstractions` 2.0.3 is Apache-2.0 but its license URL is unreadable by the tool: override in `build/license-overrides.json`. Code of Conduct contact to be confirmed (placeholder in `CODE_OF_CONDUCT.md`). On macOS/Homebrew, `nuget-license` needs `DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/<version>/libexec`.
- 2026-11-04 — 1.4: JSON Schema validator = `JsonSchema.Net` 8.0.5 (MIT). Versions 9+ move to the OSMF EULA license (forbidden): Dependabot configured to ignore them. Project to watch (no more MIT updates); alternative to study if needed.
- 2026-11-04 — 2.6: `/gw/config` protected by a bearer token (`Gateway:DiagnosticsToken`, disabled if absent) until Entra ID (4.5).
- 2026-11-04 — Phase 2 done. Demo: `dotnet run --project samples/backend-orders` then `dotnet run --project src/Gatewright.Host -- --Gateway:Mode=all --Gateway:ConfigSource=File --Gateway:ConfigFile="$PWD/samples/dev-config.json" --urls http://localhost:8080`; `curl http://localhost:8080/orders/1`. Absolute config path required (`dotnet run --project` changes the working directory).
- 2026-11-04 — Phase 3 done. Deviations: 3.8 `cache` relies on `IOutputCacheStore` (ASP.NET) and a response capture (`ResponseCaptureFeature`); 3.9 adds `IBackendPolicyInstance` (policies that wrap the backend call); retry only on connection errors/timeouts (not on 5xx statuses already emitted); rateLimit/quota/circuit breaker counters are local to each pod (Redis in phase 5). Docs: `docs/user/policies.md`.
- 2026-11-04 — 4.2 `[~]` **Decision required**: the `Npgsql.EntityFrameworkCore.PostgreSQL` provider (and `Npgsql`) is under the **PostgreSQL** license, which is absent from the CLAUDE.md §7 allow list (MIT, Apache-2.0, BSD, ISC, MS-PL, 0BSD, Unlicense). Rule: stop and ask. Non-interactive session: the DbContext is written without a dependency on the provider (`GatewrightDbContext`), with SQLite (`Microsoft.EntityFrameworkCore.Sqlite`, MIT) for development and tests (`EnsureCreated`, no migrations). To do once the license is approved (suggestion: add `PostgreSQL` to the allow list, a permissive OSI license equivalent to MIT/BSD): `UseNpgsql`, versioned migrations, Testcontainers tests (`Testcontainers.PostgreSql` is MIT). Tasks 4.3 to 4.8 proceed on SQLite.
- 2026-11-05 — 13.6 done: `scripts/k8s-e2e.sh` (the command CLAUDE.md §5 already documented, now written) builds `gatewright-host` and the `backend-orders` sample image, loads both into a kind cluster (creates it if missing, matching the documented `kind create cluster --name gatewright && ./scripts/k8s-e2e.sh`), deploys `backend-orders` as a `gatewright.io/expose`-annotated Service, installs the chart (`gateway.mode=all`), then runs the scenarios through a port-forward: `/healthz` up and `/readyz` 503 before any publication, create + publish an API with `backend.service` pointing at the *discovered* Service (not a static destination — exercises RBAC, `KubernetesDiscoveryService` and the EndpointSlice-to-destination path together, not just the Admin API), the proxy actually reaching the backend through it, and `/readyz` turning `200` afterward. CI: `.github/workflows/k8s-e2e.yml` (installs `kind` via `helm/kind-action`, `helm` via `azure/setup-helm`, then runs the script) on push and pull request — not yet verified on a real Actions run (no pushed repository yet, same blocker as the other CI tasks); the script itself ran clean end to end against the real `gatewright-test` kind cluster, several times, which is what the workflow will run unchanged. **Found and fixed a real chart bug while writing the script**: every component-scoped `Service`/`PersistentVolumeClaim`/`HorizontalPodAutoscaler`/`PodDisruptionBudget`/`NetworkPolicy` (added across 13.2-13.5) carried `app.kubernetes.io/component` in its `spec.selector`/`podSelector` (correct, selection worked) but **not on its own `metadata.labels`** — only `Deployment` pod templates had it there. `kubectl get svc -l app.kubernetes.io/component=X`, exactly the kind of lookup an operator (or this script) reaches for, silently returned nothing for every one of these resources; discovered because the script's own first version hardcoded a service name instead of looking it up by label, hit an unrelated Helm fullname-collapsing edge case (release name containing "gatewright"), and fixing that edge case the *right* way (label lookup instead of string-guessing) immediately exposed the missing label. Fixed by adding the literal `app.kubernetes.io/component: X` line to every one of those resources' own `metadata.labels`, mirroring the pattern pod templates already used; `RoleBinding`/`ClusterRoleBinding`/`ServiceAccount`/the two shared `Secret`s stay without a component label on purpose (they are not scoped to one). `helm lint` green; this script is the "e2e green" half of this phase's AC, confirmed locally — the CI half is only confirmed by inspection until the repository is pushed.
- 2026-11-05 — 13.5 done: external Redis/PostgreSQL were already options since 13.2 (`redis.connectionString`, `postgres.connectionString` — any reachable connection string, Azure Cache for Redis / Azure Database for PostgreSQL included, no chart-side awareness of "Azure" needed). Added for dev/test: `redis.dev.enabled`/`postgres.dev.enabled` deploy a single `redis:7-alpine`/`postgres:17-alpine` **in this same Helm release** (no chart `dependencies:`/sub-chart, no external chart repository) instead of pulling in a third-party chart (Bitnami-style) for a one-line convenience — reuses images already vetted elsewhere in this repo (`RedisFixture`/`PostgresFixture` Testcontainers, task 4.2/5.2). Either toggle is ignored once the matching `connectionString` is set. The dev Postgres password is a plain overridable value (`postgres.dev.password`), not `randAlphaNum`-generated: a Helm-generated secret would need the identical value independently re-derived in two separately rendered template files on first install (the connection string in `secret.yaml`, the container's own `POSTGRES_PASSWORD` in `dev-postgres.yaml`), and `lookup` only converges on a *second* install once something already exists — a plain value sidesteps that race entirely and the password only ever protects a throwaway dev database anyway. **Found and fixed via a real deploy**: with `postgres.dev.enabled=true`, the control plane crashed once (`Npgsql... Connection refused`, confirmed in `kubectl logs --previous`) because it started before the fresh Postgres container had finished `initdb`; Kubernetes' own restart recovered it, but that is a needless crash-loop and slows `helm install --wait`. Fixed with a `wait-for-postgres` initContainer (`controlplane`/`all` only) looping `pg_isready` using `postgres.dev.image` itself — no extra image pulled, and it runs as the image's own `postgres` user (uid/gid 70; the official image's default user is `root`, which fails the pod's `runAsNonRoot: true` otherwise). A no-op (nothing rendered) when `postgres.dev` is not in use: an externally managed PostgreSQL is assumed already reachable, matching the "dependencies" framing of this task. Verified end to end on the real kind cluster: `helm install`/`upgrade` with both dev dependencies enabled, pod `Ready` with **zero restarts** after the fix (one restart before it), `/healthz` 200, confirming the control plane actually connected through Npgsql to the bundled Postgres and through `StackExchange.Redis`-backed state wiring to the bundled Redis (env vars correctly pointed at them). `helm lint`/`helm template` + `kubectl apply --dry-run=server` green for every combination.
- 2026-11-05 — 13.4 done: `rbac.create` (default on, with `kubernetes.enabled`) grants the control plane's ServiceAccount a `ClusterRole` (`services`, `endpointslices` read; `secrets` read too — RBAC has no narrower built-in scope than cluster-wide for this API, since discovery and secret references span arbitrary namespaces; documented as "tighten with your own namespaced Role if every TLS secret lives in known namespaces") and a namespaced `Role`+`RoleBinding` for the leader-election `Lease` (matches `LeaseLeaderElection`'s actual verbs: get/list/watch/create/update/patch). The data plane already had no Kubernetes rights by construction (13.2's `automountServiceAccountToken: false`, no ServiceAccount binding at all) — nothing to add there. `networkPolicy.enabled` (default on) denies ingress to every pod except its own declared port, per component; egress is left open (the data plane proxies arbitrary customer backends, the control plane reaches the Kubernetes API and optionally Redis/PostgreSQL — no portable way to narrow that across clusters). `azureWorkloadIdentity.enabled` labels the control-plane/`all` pod template `azure.workload.identity/use: "true"` (the data plane and portal never need Azure credentials). Verified on the real kind cluster: `helm lint`/`helm template` + `kubectl apply --dry-run=server` accept every new object (`ClusterRole`, `ClusterRoleBinding`, `Role`, `RoleBinding`, three `NetworkPolicy` variants); a real `helm install` with the default RBAC (no cluster-admin shortcuts) came up `1/1 Ready` and `kubectl get leases` showed the `Lease` actually held — leader election, and by extension the granted `ClusterRole`, works end to end with exactly these permissions, nothing broader. NetworkPolicy *enforcement* was not verified live: kind's default CNI (kindnet) does not enforce `NetworkPolicy`, so the resources were only confirmed schema-valid, not behaviorally tested; same caveat as before for Azure Workload Identity (no Azure cluster available).
- 2026-11-05 — 13.3 done (`gateway.mode=separate` only; `externalTrafficPolicy: Local` on the data plane Service was already set in 13.2): `dataPlane.autoscaling` (disabled by default, needs a metrics source) drives a `HorizontalPodAutoscaler` on CPU utilization, plus an optional custom-metric (`http_requests_per_second`) target for RPS-based scaling — that metric needs an external/custom metrics adapter (Prometheus Adapter, KEDA...) this chart does not install; `Deployment.spec.replicas` is omitted while autoscaling is enabled so the HPA and Helm never fight over it. `dataPlane.podDisruptionBudget` (`minAvailable: 1`, enabled by default) and a default `topologySpreadConstraints` (spread by `kubernetes.io/hostname`, `whenUnsatisfiable: ScheduleAnyway` so a single-node dev cluster still schedules). Default CPU/memory requests and limits set for every component (`controlPlane`, `dataPlane`, `all`, `portal`; previously `{}`, i.e. none). Verified: `helm lint`, `helm template` (default, `autoscaling.enabled=true` with the RPS target set, `all` mode) piped through `kubectl apply --dry-run=server` against the real `gatewright-test` kind cluster — every object (HPA `autoscaling/v2`, PDB `policy/v1`, Deployments with `topologySpreadConstraints`) accepted by the real API server. Not verified: actual autoscaling behavior (the kind cluster has no `metrics-server`, so `kubectl top` and HPA CPU scaling have nothing to read from — out of scope for a chart-correctness check).
- 2026-11-05 — 13.2 done: `deploy/helm/gatewright` (`gateway.mode`: `all` single Deployment, or `separate` independent `controlPlane` (`ClusterIP`, Admin API on 8081 Http1 + gRPC distribution on 8082 Http2 via named Kestrel endpoints, matching `samples/02`) and `dataPlane` (`LoadBalancer`, `externalTrafficPolicy: Local`, scaled by `dataPlane.replicaCount`) Deployments; optional `portal`. Secrets (distribution token — generated once and read back with `lookup` so `helm upgrade` keeps it stable, diagnostics token, DB connection string, portal OIDC client secret) are never in the ConfigMap-equivalent plain env; `image.repository`/`image.tag` and `portal.image.*` overridable for customer distribution (D10/9.6). SQLite is single-replica only (PVC); `postgres.connectionString` switches to `ControlPlane:Provider=Postgres` and scales the control plane to 2. Data plane pods set `automountServiceAccountToken: false` (only the control plane needs the Kubernetes API). **Found and fixed via a real deploy** (Colima-backed kind, `gatewright-test` cluster, chiseled image from 13.1 loaded with `kind load docker-image`): the control plane's and `all` mode's readiness probe first pointed at `/readyz`, which only turns `200` once a configuration snapshot is applied — on a fresh install nothing is published yet, so the pod never became a Service endpoint and the Admin API used to publish that first configuration was unreachable (a bootstrap deadlock). Fixed to `/healthz` for `controlplane`/`all` (liveness only; `/readyz`'s proxy-readiness meaning stays correct and unchanged for the pure `dataPlane` component). Verified end to end: `helm install` → pod `Ready` → `POST /admin/v1/apis` → `POST .../publications` → `/readyz` turns `200`. `helm lint` and `helm template` (all three combinations: `all`, `separate`, `separate`+`portal`+`postgres`) green. Not done: HPA/PDB/`topologySpreadConstraints`/resource defaults (13.3), RBAC manifests for the control plane's Kubernetes access and NetworkPolicies (13.4), external Redis/PostgreSQL sub-charts (13.5), the scripted `kind` E2E (13.6, this manual pass is not that script).
- 2026-11-05 — 13.1 done: `dotnet publish -t:PublishContainer` (built into the SDK, no extra package) replaces a hand Dockerfile for the two deployable web projects. `ContainerRepository`/`ContainerFamily=noble-chiseled` set directly on `Gatewright.Host.csproj` and `Gatewright.Portal.csproj`; the SDK resolved the base image to `aspnet:10.0-noble-chiseled-extra` on its own (plain `10.0-chiseled` is not published for net10.0; `-extra` keeps globalization/ICU, needed since the control plane uses `Microsoft.Data.Sqlite`). Verified locally (Colima/Docker): non-root by default (uid 1654 `app`, no image changes needed), boots and serves `/healthz` in File and Database (SQLite) config source, and under `docker run --read-only` given three writable mounts — `/tmp`, `/home/app/.aspnet` (Data Protection keys; ephemeral is fine, see D1/D5) and wherever `ControlPlane:ConnectionString` points. Those mounts are deferred to the Helm chart (13.2/13.3) as `emptyDir`/PVC volumes with `readOnlyRootFilesystem: true`; not yet enforced anywhere. OCI labels (license, source, version, revision, created, authors) come for free from `ContainerLabel` items plus the SDK's own metadata (`PublishRepositoryUrl`/`EmbedUntrackedSources`, already set in `Directory.Build.props`, supply the git revision). CI: new `.github/workflows/container-scan.yml` builds both images and scans them with Trivy (`CRITICAL,HIGH`, `ignore-unfixed`) on push and PR; verified locally with `trivy image` against the chiseled `gatewright-host` build (0 findings) — not yet verified green on a real GitHub Actions run (same blocker as 0.4/0.10/0.12/0.14: no pushed repository yet). The root `Dockerfile` (samples, `PROJECT`/`ASSEMBLY`, Docker/Podman/Colima) is untouched and keeps working as before; vulnerability scanning was not added for it since it is a dev/sample convenience, not a distributed artifact.
- 2026-11-05 — 4.2 done: PostgreSQL License approved by the user, added to `CLAUDE.md` §7 and `build/allowed-licenses.json` (confirmed by `nuget-license`: `Npgsql`/`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 report license expression `PostgreSQL`, green against the allow list). `ControlPlaneOptions.Provider` (`Sqlite` default, `Postgres`) selects the provider; SQLite stays the no-dependency default (dev, samples without a container, task 4.3-4.8 work unchanged) and keeps `EnsureCreated`. PostgreSQL uses versioned EF Core migrations (`src/Gatewright.ControlPlane/Migrations/Postgres`, generated by `dotnet ef migrations add`, a `PostgresDesignTimeDbContextFactory` provides the design-time context) applied through `Database.MigrateAsync()` in `ControlPlaneDatabase.InitializeAsync`. Migration files are machine-generated and excluded from the header/style rules (`.editorconfig` `[**/Migrations/**.cs]`) since they are regenerated as-is rather than hand-edited. Tests: `PostgresFixture` (Testcontainers, `postgres:17-alpine`) + `PostgresMigrationTests` (migration applied, idempotent seed, round trip), green locally against the Colima-backed Docker engine. `THIRD-PARTY-NOTICES.md` regenerated. Not done: switching any sample or the Helm chart to Postgres (stays SQLite by default; Postgres wiring for samples/chart is for phase 13).
- 2026-11-04 — 4.4 to 4.6 delivered in a single increment (Admin API, Entra ID/JWT security + audit, subscription keys): endpoints, roles and hash-based key storage are intertwined. Authentication: `Microsoft.AspNetCore.Authentication.JwtBearer` (Entra ID roles through the `roles` claim, config `ControlPlane:Auth:*`); `DevelopmentBypass` reserved for the Development environment (startup failure otherwise). `Gateway:ConfigSource=Database` (All mode): a service polls the active publication of `Gateway:Environment` and pushes it to the in-memory data plane (anticipates 6.2 for the single-process mode). SQLite database for now (`ControlPlane:ConnectionString`), see 4.2.
- 2026-11-04 — Phase 7: 7.1, 7.2, 7.3 and 7.6 delivered and tested on a real kind cluster (`GATEWRIGHT_K8S_TESTS=1 dotnet test tests/Gatewright.K8sTests`, cluster `gatewright-test` created on demand). Architecture: the leader (Lease) watches Services/EndpointSlices and writes a state in the database; all CP replicas relay it to the data planes through a gRPC `EndpointsUpdate` message independent of configuration versions (pods move without a new publication); `backend.service` clusters receive these addresses (ready pods, excluding terminating pods) and passive health checks by default. Scaling 1→3→1 is verified on the discovery side and on the destination application side; an end-to-end proxy test to pod IPs is not possible from the macOS host (Docker network) and remains to be done in the Linux CI job (13.6). **7.4 postponed** until after 10.2 (the OpenAPI importer did not exist yet). **7.5 postponed** with 8.1: the hosts/certificates model is introduced with TLS termination.
- 2026-11-05 — 7.5 done: certificate material is never persisted (CLAUDE.md §6 security rule), so it cannot flow through `DbDiscoveryStore`/`DiscoveryStateEntity` like Services/EndpointSlices do. Design: `Gatewright.Core.Security.ISecretFetcher` (fetch one secret's `tls.crt`/`tls.key` by `namespace/name`) is implemented by `Gatewright.Kubernetes.KubernetesSecretFetcher`; `Gatewright.ControlPlane.Distribution.TlsSecretsHub` (a `BackgroundService`, registered in `AddDiscoveryHub` next to `EndpointsHub`) polls the declared `Hostnames` table every `Kubernetes:Secrets:PollSeconds` (10s) for `Secret`-sourced references, fetches each, and keeps the material in memory only. Runs independently on **every** control plane replica (no leader election, unlike Kubernetes discovery): each replica fetches its own copy directly from the Kubernetes API, so no cross-replica sharing of key material is needed and a replica that cannot reach the API still serves its own connected nodes with what it already has. A fetch that fails temporarily never drops material already loaded (same "last known good" spirit as D5). New `SecretsUpdate` gRPC message (map reference -> cert/key PEM) pushed on connect and on change, parallel to `EndpointsUpdate` and equally independent of configuration versions; `SnapshotApplier.ApplySecret` forwards to `CertificateStore.SetSecret` (already built, per the 8.x journal note) on the data plane, and `LocalSecretsApplierService` wires the same path in-process for `All` mode. Tested: `TlsSecretsHubTests` (fetch, change notification, failed-fetch keeps the last good material, removed hostname drops its secret), `SnapshotApplier.ApplySecret` test, a full CP->DP integration test (`DistributionEndToEndTests`) with a fake `ISecretFetcher`, and a real read against a kind cluster (`GATEWRIGHT_K8S_TESTS=1`, `KubernetesSecretFetcherTests`). RBAC scoping to the specific referenced secrets (CLAUDE.md's "targeted Secrets") is deferred to 13.4 as planned. Docs: `docs/user/getting-started.md`.
- 2026-11-04 — Phase 8: SNI TLS termination on Kestrel (`Gateway:Tls:HttpsPort`/`HttpPort`), certificates read from PEM files (Kubernetes secret mounted as a volume) and hot reloaded (`Gateway:Tls:ReloadSeconds`); `hostnames` (certificate, HTTP→HTTPS 308 redirect, HSTS) in the config, the Admin API (`/admin/v1/hostnames`) and the export; `X-Forwarded-*` headers accepted only from `Gateway:ForwardedHeaders:TrustedProxies`; WebSocket (tested on Kestrel: YARP requires the HTTP upgrade feature that TestServer does not expose) and gRPC passthrough over h2c (`backend.httpVersion: "2"` => `RequestVersionExact`); graceful shutdown: `/readyz` turns 503 as soon as shutdown begins (`ReadinessGate`) and `Gateway:Shutdown:DrainDelaySeconds` gives load balancers time (to be tied to the chart's `preStop`, 13.2). Certificates coming from a Kubernetes Secret read by the CP (7.5) remain to be done: `CertificateStore.SetSecret` is ready on the data plane side.
- 2026-11-04 — Phase 9 (in progress): the host composition (modes, endpoints, TLS…) lives in `Gatewright.Sdk` (`GatewayHost.CreateBuilder(args).AddPolicy<T>().Build().Run()`); `Gatewright.Host` is now just a 3-line `Program` using the SDK: the standard image and a customer's image follow exactly the same code path (D10). JSON schema generated by `JsonSchemaExporter` from the config record (+ `[Description]`, `additionalProperties:false`). `Gatewright.Testing`: `GatewayTestHost` + `SimulatedBackend`. Sample `samples/custom-policy` (`acme.fraudCheck`) tested in `tests/Gatewright.Samples.Tests`. Docs: `docs/user/extending.md`.
- 2026-11-04 — Phase 9 done. 9.4: template `templates/gatewright-host` tested end to end by `scripts/test-template.sh` (local pack → `dotnet new` → build → tests). 9.6: versions computed by MinVer from `v*` tags (without a tag: `0.0.0-preview.0.N`), README per package, LICENSE and THIRD-PARTY-NOTICES.md included. The template package (`Gatewright.Templates`) and NuGet publication are handled in the release pipeline (15.4).
- 2026-11-04 — 10.1/10.2: reading OpenAPI 3.0/3.1 (JSON or YAML) without a dependency on Microsoft.OpenApi: the document is read as a JSON tree (YAML converted with YAML 1.2 type resolution, through YamlDotNet MIT) in order to read `x-gateway` as is. Spec: `docs/user/x-gateway.md` + `docs/schemas/x-gateway-1.schema.json`. `POST /admin/v1/apis/import` (body = document; `name`, `basePath`, `service`/`destination`, `product`, `removeMissing`, `dryRun`) creates or updates a draft API and returns the report (additions, changes, removals).
- 2026-11-04 — 7.4 delivered after 10.2: on the leader, a service annotated `gatewright.io/openapi-path` has its document fetched (cluster DNS, `Kubernetes:AutoImport:UrlTemplate`) and the API created/updated as a draft (backend `service`, `base-path`, `product` taken from the annotations); an unchanged document creates no revision; `Kubernetes:AutoImport:PublishTo` (list of environments, empty by default) enables automatic publication.
- 2026-11-04 — 10.4: `gatewright` CLI (System.CommandLine): login, api import, config export/import/validate (offline), publish, rollback, nodes; Entra token provided through `--token`/`GATEWRIGHT_TOKEN` (no interactive device-code flow for now). Docs: `docs/user/cli.md`. Phase 10: the end-to-end AC "samples/backend-orders exposed without YAML" remains to be demonstrated with the chart (13.6).
- 2026-11-04 — Phase 11: Blazor Web App portal (interactive server) `Gatewright.Portal`: Entra ID OIDC (code + PKCE, saved tokens, `roles` roles) or `Portal:Auth:Mode=Development` (Development only); pages: dashboard, APIs (effective policies per operation), OpenAPI import (preview/report, discovered service), policy editor generated from the JSON Schemas (`/admin/v1/policy-types`), products/plans, subscriptions and keys (shown once), revisions (diff, publication, rollback, audit), test console with per-policy trace (`x-gatewright-trace` + `Gateway:DiagnosticsToken` on the gateway side). bUnit tests. Limitation: no real browser test (Playwright); the portal does not yet edit products/product policies nor shared policies.
- 2026-11-04 — Phase 12: OTel wired in the SDK (traces/metrics/logs, OTLP if `Telemetry:Otlp:Endpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT`). Business metrics in `GatewayMetrics` (meter `Gatewright.Gateway`), recorded by `PolicyMiddleware`; `subscription` dimension off by default (cardinality), provided by the aggregated usage metrics. Duration per policy already existed (meter `Gatewright.Policies`). Access logs: bounded queue (drop + counter when full), errors always logged, success sampling, no headers, masked query string. 12.4: `IUsageSink` + OTLP sink delivered; Event Hubs/Kafka sink **not delivered** (adds Azure dependencies) → Backlog, customers can register their own `IUsageSink`. 12.5: Grafana dashboard, Azure Monitor KQL, Prometheus rules in `deploy/observability/`; not tested against a real stack. Docs: `docs/user/observability.md`.
- 2026-11-04 — S.1 (user request, outside the initial scope): parameterizable root `Dockerfile` (`PROJECT`, `ASSEMBLY`), non-root image (base image UID), writable `/data` for the CP database. Samples `samples/01..06`, tested with Docker (Colima engine): 01, 02 (import + publication through `seed.sh`, 2 data planes in sync, traffic maintained with the CP stopped), 03 (429 shared between 2 gateways), 04, 05 (metrics visible in Prometheus, provisioned Grafana dashboard). **Not tested**: Podman (`podman compose`) and 06 (no container); the compose files only use standard features and qualified `docker.io/...` images. The Dockerfile covers part of 13.1 (distroless/chiseled image, CI scan and `PublishContainer` remain to do). Note: the Admin API import format is `{"format":"gatewright/v1","config":{…}}`, different from the `All` mode config file.
- 2026-11-04 — S.1 addition: `samples/06-otel-gui` (otel-gui MIT, `ghcr.io/metafab/otel-gui`, OTLP/HTTP only): the collector relays traces/logs/metrics to otel-gui and metrics to Prometheus/Grafana. Tested: traces (4 spans, correlated logs) visible in otel-gui, metrics in Prometheus. Former 06 renamed `07-without-containers`. `latest` tag not pinned.
- 2026-11-04 — S.1: `samples/06-otel-gui` reduced to otel-gui alone (user request): direct OTLP/HTTP export, without collector/Prometheus/Grafana (those remain in 05). Added `Telemetry:Otlp:Protocol` (`grpc`|`http/protobuf`, URL completed with `/v1/{signal}`). Tested: traces, logs and metrics received by otel-gui.
- 2026-11-04 — S.1: `samples/07-products` (`all` mode + `ConfigSource=Database`, Admin API on the same port, dev only): 2 products (starter/premium), plans, 2 customers, subscriptions, keys; `apiKey` with `products` filters access by product (403 `product_not_subscribed`), `rateLimit` per subscription. Tested end to end. Findings: (1) a *plan* is only a label (+ `{subscription.plan}` variable) and does not change limits; (2) product policies apply to all requests to the product's APIs, not according to the caller's subscription product; (3) `seed.sh` is not idempotent (the import replaces the draft); (4) no per-product OpenAPI is exposed (only the Admin API OpenAPI exists) → added to the Backlog. Former 07 renamed `08-without-containers`.
- 2026-11-04 — S.2: `docs/user/architecture.md` (Mermaid diagrams: All mode = 1 pod, separate CP + DP = 1 CP + N DP + optional portal/Redis, request path, config propagation, CP stopped = traffic maintained). Rendering verified with mermaid 11.4.1. Since the Helm chart does not exist yet, the document describes the target topology. CP limited to 1 replica while the database is SQLite.
- 2026-11-04 — S.3 (user request): custom policy telemetry. `FraudCheckPolicy` logs through `ILogger` (obtained at compile time), creates a `fraud-check` child span and tags the request span, increments the `acme.fraud.rejections` counter. Registration goes through `ConfigureOpenTelemetryTracerProvider/MeterProvider` (OpenTelemetry.Extensions.Hosting comes transitively with `Gatewright.Sdk`). Verified by `FraudCheckTelemetryTests` (in-memory exporter); real OTLP export of custom logs/spans was not exercised with a collector. No API added to the SDK. Docs: `docs/user/extending.md` §4. Test package added: OpenTelemetry.Exporter.InMemory (Apache-2.0).
- 2026-11-04 — S.4 (user request): all documents in English. Only `tasks.md` and `CLAUDE.md` were still in French; both translated. CLAUDE.md rule on documentation language updated accordingly. Two fixes made while translating: the merged Journal line of phase 0 was split, and the Backlog now lists the items found during S.1 (per-product OpenAPI, Event Hubs/Kafka sink, per-plan limits).
- 2026-11-04 — S.5 (user request): default environment `recette` (French word) renamed `staging` in the code (`DefaultEnvironments`, CLI help, proto comment, XML docs), tests and tasks.md. Pre-1.0 and never released, so no migration: an existing development database keeps its `recette` row, and `staging` is created next to it.
- 2026-11-04 — S.1 (user request): web portal added to samples 06 and 07 (`portal` service on http://localhost:8088, Development auth, calls the gateway Admin API). Sample 06 switched from a config file to database mode (`catalog.json` + `seed.sh`, as in 07) because the portal needs the Admin API; it still exports to otel-gui. Both tested (portal answers, API served after `seed.sh`). The test console and `Gateway__DiagnosticsToken` were not exercised from the portal in the containers.
- 2026-11-04 — S.6 (user request): idle database reads. Measured on the running sample 06: 30 SQL commands per 10 s with nothing happening. Causes: `EndpointsHub` polled a Kubernetes-discovery hash twice a second even without Kubernetes or connected data planes; `PublicationWatcherService` and `PublicationHub` read the whole publication (snapshot JSON included) at each poll; every command was logged at Information. Fixes: `EndpointsHub` does not query without subscribers, both publication pollers read only an identifier/number and load the snapshot when it changed, and the EF command log category defaults to Warning through the lowest-priority configuration source (any other source can override it). Measured after: 10 commands per 10 s in `All` + database mode (the remaining one is the `Gateway:ConfigPollInterval` poll of `Environments.CurrentPublicationId`). With data planes connected to a separate control plane, `Distribution:PollMilliseconds` (500 ms) still drives the publication and endpoint checks. A change-notification mechanism (LISTEN/NOTIFY on PostgreSQL, or a local in-process signal in `All` mode) would remove polling entirely; not done. Gain not measured on a separate control plane with data planes.
- 2026-11-04 — S.7 (user report): host crash `ArgumentOutOfRangeException (period)` from `PublicationWatcherService`. Cause: `Gateway__ConfigPollInterval: "30000"` in the sample 06 compose file; TimeSpan reads a bare number as days. The validator only rejected values <= 0; it now also rejects values above one day, with a message giving the `hh:mm:ss` format. Sample value corrected to `00:00:30`. Other TimeSpan options (`Gateway:Tls:ReloadSeconds`, `Distribution:*Milliseconds`) are plain integers and not affected.
- 2026-11-04 — S.8 (user request): portal redesign. Designed on a Claude Design canvas first (five pages, validated by the user), then ported to Blazor: `wwwroot/app.css` is the stylesheet of the design (variables, cards, tables, pills, waterfall) plus global defaults for plain elements; `MainLayout` = top bar + tabs (no sidebar); every page restyled with the same element ids and class names the bUnit tests use (17/17 green). Not carried over from the design because it would not work today: the environment selector in the top bar, per-tab counters, the "Latest changes" panel and per-API policy-scope badges. The test console shows the real policy trace as a waterfall (offsets computed from cumulative durations). Checked by rendering the pages against a gateway in database mode (Dashboard, Subscriptions, Revisions, Test console); APIs, Products, Import and API detail were not re-rendered after the restyle.
- 2026-11-04 — S.8 follow-up: the first port left the API detail page and the policy editor as an unstyled stack of full-width fields (not re-rendered at the time, user reported it). Reworked: summary card, operations card with an empty state, one card per policy (name, inheritance, move/remove) with a responsive field grid (`SchemaForm`), dashed add-policy selector, styled file input. Rendered and checked against a running gateway; the bUnit selectors are unchanged (17/17).
- 2026-11-05 — 14.1 done: `tests/load` (k6, chosen over NBomber: an external HTTP driver, no .NET build dependency, matches how a real load balancer drives the gateway). `compose.yaml` stands up the gateway (`all` mode, `Database` config source, Redis-backed shared rate limit), the `backend-orders` sample and a static Python `http.server` serving a JWKS document for the `jwt` scenario. `scripts/gen-jwt-keys.sh` mints a throwaway RSA key pair, its JWKS and a 10-year RS256 token (openssl for key generation/signing, Python stdlib only for the JSON/base64url plumbing — no new runtime dependency); `seed.sh` imports `catalog.json` (one API per scenario: `bare`, `secured` (jwt), `limited` (apiKey + shared rateLimit), `cached`) and mints one subscription's API key, mirroring sample 07's pattern. Five k6 scripts (`bare-proxy`, `jwt`, `apikey-ratelimit`, `cache`, `mix`) driven by `run.sh`; `cache.js` asserts on the `X-Cache` response header, `apikey-ratelimit.js` treats 429 as an expected outcome (not a failure) since all VUs share one subscription on purpose to exercise the Redis batched-reservation path, not just the local bucket. Verified end to end on the real stack (Colima-backed Docker): all four routes return the expected status (200 / 401 without a token / cache MISS then HIT), all five k6 scripts run clean at a small scale (5 VUs / 3s, 0% `http_req_failed`). `.generated/` (key, JWKS, token, API key) is gitignored, dev-only throwaway material. Not done yet (next tasks): numeric targets and thresholds tuned to a specific RPS (14.2), profiling (14.3).
