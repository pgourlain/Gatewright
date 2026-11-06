# CLAUDE.md — Gatewright (APIM + YARP reverse proxy for Kubernetes)

> Product name: **Gatewright** (root namespace `Gatewright`, packages `Gatewright.*`, CLI `gatewright`, annotations `gatewright.io/*`). Project intended to be published as open source.

## 1. Product vision

Gatewright is an **API Management + reverse proxy** based on **YARP**, designed to run in Kubernetes and to act as the **cluster entry point** (its own ingress, with no mandatory third-party controller).

Non-negotiable goals:
1. **Scalable**: stateless data plane, unlimited horizontal scaling, the request path depends neither on the Kubernetes API nor on the control plane.
2. **Simple declaration, no YAML**: web portal, enriched OpenAPI import, .NET code-first. **No dependency on CRDs or on the cluster's Gateway API version.**
3. **Extensible in C#**: a customer installs Gatewright on their Kubernetes and adds their own policies through a NuGet SDK ("distribution" model).
4. **A single binary**, several modes (`all`, `control-plane`, `data-plane`), usable in dev in a single pod or even without Kubernetes.

## 2. Architecture

```
Web portal ──┐
CLI / CI ────┼─► Admin API ─► Control plane ─► PostgreSQL database (APIs, policies, products,
Auto-discovery┘                │                 subscriptions, revisions, environments)
(annotated Service + OpenAPI)  │ watch Services / EndpointSlices / TLS Secrets
                               │ (Kubernetes = discovery source only)
                               ▼
                     Versioned config snapshot
                               │ gRPC stream (snapshot + deltas)  |  in-memory Channel (all mode)
                               ▼
LB ──► Data plane (YARP, N pods, HPA) ──► backend pods (direct LB through EndpointSlices)
             │ local cache: compiled config, API keys, JWKS, certificates
             └► Redis (batched quotas, pub/sub revocations) — minimal access
```

### Structuring decisions (do not challenge without explicit approval)

| # | Decision | Reason |
|---|---|---|
| D1 | Source of truth = control plane database, **not** CRDs | Simplicity for web technicians, independence from Kubernetes versions |
| D2 | Kubernetes is used only for discovery (Services annotated `gatewright.io/expose`, EndpointSlices, TLS Secrets) | Also works outside Kubernetes |
| D3 | Control plane / data plane separation, same binary, selected by `Gateway:Mode` | Scalability + simple dev |
| D4 | CP → DP distribution through **custom gRPC** (versioned snapshot + deltas), not xDS | Simple to evolve in C# |
| D5 | The data plane keeps the **last known good config**; a CP outage never stops traffic | Resilience |
| D6 | Policy pipeline by phases (`Inbound`, `Backend`, `Outbound`, `OnError`) with scope inheritance `global → gateway → product → API → operation` | Familiar model (Azure APIM), per-customer differentiation |
| D7 | Policies are **compiled when the config is loaded**, never interpreted per request | Performance |
| D8 | Rate limiting: local token bucket + token batch reservation in Redis; strict Lua counters only for billable quotas | Redis scalability |
| D9 | V1 extensibility = built-in policies + **.NET plugins through the distribution model** (NuGet + customer image). No dynamic DLL loading. CEL expressions in V2, WASM later | Robustness |
| D10 | One image (the customer's if customized) for CP and DP, so that the CP knows the schemas of custom policies | Consistent validation |
| D11 | Public TLS can be terminated upstream (Front Door / App Gateway); Gatewright can also terminate TLS (SNI, hot reload) | Azure context |

## 3. Technical stack

- **.NET 10 (LTS)**, latest C#, `Nullable` enabled, `TreatWarningsAsErrors` enabled.
- **YARP** (`Yarp.ReverseProxy`) for the proxy, `InMemoryConfigProvider` fed by the snapshots.
- **ASP.NET Core**: Kestrel, JWT authentication (Microsoft.Identity.Web for Entra ID), OutputCache.
- **gRPC** (`Grpc.AspNetCore`) for CP ↔ DP.
- **PostgreSQL** + **EF Core** for the control plane (versioned migrations).
- **Redis** (`StackExchange.Redis`) for the shared state of the data plane.
- **KubernetesClient** (official .NET client) for discovery and leader election (`Lease`).
- **OpenTelemetry** (traces, metrics, logs); OTLP export.
- **Portal**: Blazor (Web App, Server or Auto mode) to stay on a single stack.
- **Tests**: xUnit, FluentAssertions (or Shouldly), `WebApplicationFactory`, Testcontainers (Postgres, Redis), k6 or NBomber for load.
- **Packaging**: `dotnet publish /t:PublishContainer`, Helm chart, kind for Kubernetes integration tests.

## 4. Repository structure

```
/src
  Gatewright.Abstractions/        Stable public contracts: IGatewayPolicy, GatewayContext, PolicyResult… (strict SemVer)
  Gatewright.Core/                Config model, snapshot, validation, policy compilation, pipeline
  Gatewright.DataPlane/           YARP integration, middlewares, snapshot application, TLS/SNI
  Gatewright.ControlPlane/        Domain model, EF Core, Admin API, revisions, translation → snapshot
  Gatewright.Distribution/        CP↔DP gRPC protocol (+ in-process implementation)
  Gatewright.Kubernetes/          Services/EndpointSlices/Secrets discovery, leader election
  Gatewright.Policies/            Built-in policies (jwt, apiKey, rateLimit, cors, headers, cache, timeout, retry…)
  Gatewright.State.Redis/         Redis stores (quotas, keys, revocations)
  Gatewright.Host/                Single entry point (modes all / control-plane / data-plane)
  Gatewright.Portal/              Blazor portal
  Gatewright.Cli/                 `gatewright` CLI (OpenAPI import, config export/import, validation)
  Gatewright.Exposure/            Code-first package for .NET backends (generates x-gateway in the OpenAPI)
  Gatewright.Sdk/                 GatewayHost.CreateBuilder, GatewayPolicy<TConfig>, schema generation
  Gatewright.Testing/             GatewayTestHost for customers testing their policies
/templates
  gatewright-host/                `dotnet new` template for a customer host project
/tests
  *.Tests/                       One unit test project per src project
  Gatewright.IntegrationTests/    Testcontainers, all mode
  Gatewright.K8sTests/            Tests on kind (separate mode)
  load/                          Load scenarios
/deploy
  helm/gatewright/                Helm chart
  observability/                  Grafana dashboard, Azure Monitor queries, alert rules
/samples
  01-… to 08-…                    One directory per scenario, with compose files for Docker, Podman and Colima
  backend-orders/                 Sample .NET backend using Gatewright.Exposure
  custom-policy/                  Customer distribution example with a custom policy
/docs
  adr/                           Architecture Decision Records
  user/                          User docs (install, declaration, extension)
/.github
  workflows/                     CI, release, license check
  ISSUE_TEMPLATE/                Issue templates (bug, feature, question)
  PULL_REQUEST_TEMPLATE.md
Dockerfile                       Builds every deployable project (PROJECT / ASSEMBLY build arguments)
LICENSE                          MIT
NOTICE                           Project copyright and notices
THIRD-PARTY-NOTICES.md           Dependency licenses (generated)
CHANGELOG.md                     Keep a Changelog + SemVer
CONTRIBUTING.md
CODE_OF_CONDUCT.md               Contributor Covenant 2.1
SECURITY.md                      Vulnerability reporting
GOVERNANCE.md                    Roles, decisions, maintainers
TRADEMARKS.md                    Use of the Gatewright name and logo
MAINTAINERS.md
README.md
CLAUDE.md
tasks.md
```

## 5. Commands

```bash
dotnet build Gatewright.slnx
dotnet test Gatewright.slnx
dotnet test tests/Gatewright.IntegrationTests          # needs Docker (Testcontainers)
dotnet run --project src/Gatewright.Host -- --Gateway:Mode=all --Gateway:ConfigSource=File --Gateway:ConfigFile=$PWD/samples/dev-config.json
dotnet format --verify-no-changes
dotnet publish src/Gatewright.Host -c Release -t:PublishContainer      # builds gatewright-host (non-root, chiseled)
dotnet publish src/Gatewright.Portal -c Release -t:PublishContainer    # builds gatewright-portal (non-root, chiseled)
helm lint deploy/helm/gatewright
kind create cluster --name gatewright && ./scripts/k8s-e2e.sh  # end-to-end tests
cd tests/load && ./run.sh [bare-proxy|jwt|apikey-ratelimit|cache|mix]   # k6 load scenarios, see tests/load/README.md
kind create cluster --name gatewright && ./scripts/k8s-scale-test.sh   # 14.2: scale-out/in, rolling update, config propagation at scale
./scripts/prepare-release.sh 1.0.0       # 15.5: [Unreleased] -> [1.0.0] in CHANGELOG.md (see docs/release.md)
./scripts/extract-changelog.sh 1.0.0     # 15.4: prints the GitHub Release body for a version
```

The official images (`gatewright-host`, `gatewright-portal`, built above) run as a non-root user on a chiseled base and work with a read-only root filesystem, given three writable mounts: `/tmp`, `/home/app/.aspnet` (ASP.NET Data Protection keys; ephemeral is fine, the data plane is stateless per D1) and wherever `ControlPlane:ConnectionString` points for the SQLite provider. The Helm chart (phase 13) sets `readOnlyRootFilesystem: true` and provides these as `emptyDir` volumes (plus a PVC-backed one for the database path). The root `Dockerfile` (samples, local dev, any project through `PROJECT`/`ASSEMBLY`) is unrelated to this and keeps working as before.

If a command listed here does not exist yet (phase not yet done), do not invent it: create it in the relevant phase and update this section.

## 6. Code rules

### General
- Code, identifiers, XML comments and commit messages **in English**. **All documentation is in English** too (`/docs`, README, `tasks.md`, this file).
- Classes `sealed` by default, `record` for immutable models (config, snapshots).
- Dependency injection everywhere, no mutable statics.
- Typed options (`IOptions<T>`) validated at startup (`ValidateOnStart`).
- No exceptions for control flow on the hot path.
- One file per public type. No `#region`.

### Hot path (data plane, per request) — strict rules
- **No call** to the Kubernetes API, the control plane or the database.
- Redis only through the planned stores (batched reservation, no per-request call except an explicitly configured strict quota).
- No allocating LINQ, no reflection, no JSON serialization of the config; prefer `ValueTask`, `Span<T>`, precomputed structures.
- Every policy has a **timeout** and is instrumented (duration, result).
- Snapshots are **immutable** and replaced atomically (`Volatile.Write` / reference swap).

### Public contracts (`Gatewright.Abstractions`, `Gatewright.Sdk`)
- Strict SemVer: no breaking change without a major version. Mark `[Obsolete]` before removal.
- Every public API has XML documentation.
- Use the `Microsoft.CodeAnalysis.PublicApiAnalyzers` analyzer (`PublicAPI.Shipped.txt` / `Unshipped.txt`).

### Control plane
- Every configuration change creates a **revision**; explicit publication to an environment; rollback possible.
- Full validation (policy schemas, route consistency, path conflicts) **before** producing a snapshot.
- Audit: who, when, what.

### Security
- Never a plaintext secret in the config or the database: references (Key Vault, Kubernetes Secret) resolved at runtime.
- API keys stored **hashed** (SHA-256 + salt/pepper), constant-time comparison.
- Admin API and portal protected by Entra ID (OIDC) with roles (`Admin`, `Publisher`, `Reader`).
- Minimal Kubernetes RBAC: only the control plane reads the Kubernetes API; the data plane has no Kubernetes rights.

## 7. Open source, license and changelog

### License
- The project is under the **MIT license**. `LICENSE` file at the root, standard MIT text unmodified.
- Copyright holder: **Pierrick Gourlain**, personal project. Copyright line: `Copyright (c) 2026 Pierrick Gourlain and Gatewright contributors`. **No mention of an employer or company** in the code, the documentation, the metadata or the commits.
- **Mandatory header** on every source file (`.cs`, `.razor`, `.proto`, scripts):
  ```csharp
  // Copyright (c) Gatewright contributors.
  // SPDX-License-Identifier: MIT
  ```
  Enforced through `file_header_template` in `.editorconfig` and rule IDE0073 as an error.
- **NuGet packages**: `PackageLicenseExpression=MIT`, `Copyright`, `Authors`, `PackageProjectUrl`, `RepositoryUrl`, `PackageReadmeFile`, `PackageIcon`, Source Link, symbols (`.snupkg`). Centralized in `Directory.Build.props`.
- **Container images**: OCI labels (`org.opencontainers.image.licenses=MIT`, `source`, `version`, `revision`).
- **Helm chart**: `annotations` field with the license, `home`, `sources`.

### Third-party dependencies
- Allowed licenses: **MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MS-PL, 0BSD, Unlicense, PostgreSQL License**.
- **Forbidden** licenses: GPL, LGPL, AGPL, SSPL, BSL/BUSL, Commons Clause, non-commercial or unlicensed.
- Every new dependency goes through the license check in CI; for a license absent from the list, stop and ask.
- `THIRD-PARTY-NOTICES.md` is **generated** by CI from the resolved dependencies (do not edit by hand).
- Never copy code from another project without checking its license and keeping the attribution in `THIRD-PARTY-NOTICES.md`.

### Contributions
- **DCO**: every commit is signed (`git commit -s`, `Signed-off-by:` line), verified in CI.
- Conventional Commits mandatory (used to generate release notes).

### Changelog
- Format **[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/)** + **SemVer**.
- `## [Unreleased]` section at the top, with the headings `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.
- **Every task that changes visible behaviour** (public API, config, CLI, Admin API, chart, policies) adds a line to `[Unreleased]`, written for a user, in English, with the task reference (e.g. `- JWT policy with background JWKS refresh (#3.4)`).
- Purely internal changes (refactoring, tests) do not go in the changelog.
- Any compatibility break is prefixed with `**BREAKING:**` and accompanied by a migration note.
- At release: `[Unreleased]` becomes `## [x.y.z] - YYYY-MM-DD`, comparison links updated at the bottom of the file, and the section serves as the body of the GitHub Release.

## 8. Tests

- Every task delivers its tests. No task checked without green tests.
- Unit tests for logic (validation, policy compilation, snapshot translation, rate limiting).
- Integration in `all` mode with Testcontainers for full journeys.
- Kubernetes tests (kind) for discovery, leader election, multi-pod distribution.
- Load tests starting from the dedicated phase, with thresholds (see tasks.md).
- Naming: `Method_Scenario_ExpectedResult`.

## 9. Way of working (instructions for Claude)

1. **Read `tasks.md`** at the start of each session and resume at the first unchecked task of the current phase. Do not skip phases.
2. Before a non-trivial task, **briefly explain the plan** (files touched, approach), then implement.
3. **One task = one coherent increment** that compiles and whose tests pass. One commit per task, Conventional Commits message (`feat(dataplane): …`), DCO signed (`git commit -s`).
4. After each task: `dotnet build`, `dotnet test`, `dotnet format`, update `CHANGELOG.md` if the change is visible (§7), then **check the task in `tasks.md`** and note any deviation or decision in the "Journal" section of `tasks.md`.
5. **Do not widen the scope**: no feature that is not listed. If a need appears, add it to `tasks.md` (section "Backlog") and ask.
6. Any decision that contradicts section 2 requires an **ADR** in `/docs/adr/` and human validation.
7. In case of blocking ambiguity, **ask the question** rather than guess. For minor choices, take the simplest option and note it in the Journal.
8. Never disable a test, an analyzer or a warning to make the build pass.
9. Prefer small, readable changes; do not reformat unrelated code.
10. Update the "Commands" section of this file when a script or command is added.

## 10. Definition of "done" for a phase

- All tasks checked, acceptance criteria verified.
- Build, tests and format green.
- Documentation (`/docs/user`) up to date for visible features.
- `CHANGELOG.md` up to date; license headers present; dependency license check green.
- Reproducible demo described in the Journal (commands to run).
