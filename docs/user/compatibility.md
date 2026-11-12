# Compatibility and versioning policy

## Semantic Versioning

Gatewright and all its NuGet packages share one version, computed from the git tag (`vMAJOR.MINOR.PATCH[-preview.N]`). Before `1.0.0`, minor versions may contain breaking changes, always listed in `CHANGELOG.md` with a `**BREAKING:**` prefix and a migration note.

| Surface | Stability promise from 1.0 |
|---|---|
| `Gatewright.Abstractions`, `Gatewright.Sdk`, `Gatewright.Testing`, `Gatewright.Exposure` (public C# API) | Strict SemVer. Tracked by `PublicAPI.Shipped.txt`; removal is preceded by `[Obsolete]` for at least one minor version |
| Configuration model (JSON snapshot / export `gatewright/v1`) | Additive within a major: new optional properties only |
| Policy configuration schemas of built-in policies | Additive within a major; a new required property or a changed meaning is a major change |
| Admin API `/admin/v1` | Additive within `v1`; breaking changes go to `/admin/v2` |
| CLI commands and exit codes | Additive within a major |
| Helm chart values | Renamed or removed values are breaking and called out in the changelog |
| gRPC distribution protocol (`protocol_version`) | Additive within a major (new fields and messages only; unknown ones are ignored) |
| `x-gateway` OpenAPI extension | Versioned (`x-gateway` version 1), see [x-gateway](x-gateway.md) |
| Metrics names and tags | Additive within a major; renames are breaking |
| Container image names and labels | Stable; tags `X.Y.Z`, `X.Y`, `X`, `latest` |

Not covered: internal projects (`Gatewright.Core`, `.DataPlane`, `.ControlPlane`, `.Distribution`, …) are packaged for the host and have no compatibility promise; build against `Gatewright.Sdk` and `Gatewright.Abstractions` only.

## Supported .NET versions

| Artifact | Target frameworks |
|---|---|
| NuGet libraries (`Gatewright.Abstractions`, `.Sdk`, `.Exposure`, `.Testing` and the packages they depend on) | `net10.0` (LTS) and `net11.0` |
| Official images `gatewright-host`, `gatewright-portal` | .NET 11, chiseled Ubuntu 26.04 base (`resolute-chiseled-extra`) |
| `gatewright` CLI (.NET tool) | `net11.0` |
| `dotnet new gatewright-host` template | generates a `net11.0` host; change `TargetFramework` to `net10.0` (and the Dockerfile tags to `10.0`) to stay on .NET 10 LTS |

Dropping a target framework from the libraries is a breaking change (major version).

## Control plane / data plane matrix

A data plane can run **one release behind** the control plane (N-1). Upgrade the control plane first.

| Control plane | Data plane N | Data plane N-1 | Data plane N-2 or older | Data plane newer than control plane |
|---|---|---|---|---|
| N | ✅ supported | ✅ supported | ⚠️ not supported (may work, not tested) | ❌ not supported |

"One release" means the previous minor within a major, and the last minor of the previous major across a major boundary. The rule holds because the distribution protocol and configuration model are additive within a major, and a major change must keep the previous protocol readable for the whole next major.

## Custom distributions

A customer image (`Gatewright.Sdk`) runs both control plane and data plane (D10), so the same image is upgraded everywhere; the matrix matters while a rolling update is in progress, or when control plane and data plane images are built from different SDK versions.

## Migration guide template

Each breaking change in `CHANGELOG.md` states: what changed, who is affected, and the exact steps. For larger changes a page is added under `docs/user/migrations/<from>-to-<to>.md` and linked from the changelog entry. See [upgrades](upgrades.md) for the generic procedure.
