# Gatewright

[![CI](https://github.com/pgourlain/gatewright/actions/workflows/ci.yml/badge.svg)](https://github.com/pgourlain/gatewright/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Gatewright.Sdk.svg)](https://www.nuget.org/packages/Gatewright.Sdk)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Gatewright is an **API Management and reverse proxy** built on [YARP](https://github.com/microsoft/reverse-proxy),
designed to run in Kubernetes as the entry point of the cluster.

- **Scalable**: stateless data plane, horizontal scaling, no dependency on the Kubernetes API or control plane on the request path.
- **Simple declaration, no YAML**: web portal, enriched OpenAPI import, .NET code-first. No CRDs, no Gateway API dependency.
- **Extensible in C#**: add your own policies through a NuGet SDK and ship your own image.
- **One binary**, several modes (`all`, `control-plane`, `data-plane`); runs in a single pod or without Kubernetes.

> Status: early development. See [tasks.md](tasks.md) for the roadmap and [CLAUDE.md](CLAUDE.md) for the architecture.

## Quick start

Run Gatewright on a local Kubernetes cluster (kind, Colima, Kiri or Podman) from the **published images**: no clone, no build.
This installs the all-in-one mode with the web portal, a try-out configuration with sign-in disabled.

**zsh**:

```zsh
GW_VERSION=1.0.0-rc.2
cat > gatewright-values.yaml <<'EOF'
gateway: { mode: all }
controlPlane: { auth: { developmentBypass: true } }  # try-out only
all: { service: { type: ClusterIP } }
portal: { enabled: true, auth: { mode: Development } }  # try-out only
EOF
helm upgrade --install gatewright oci://ghcr.io/pgourlain/charts/gatewright --version $GW_VERSION --namespace gatewright --create-namespace -f gatewright-values.yaml
```

**PowerShell**:

```powershell
$GW_VERSION = '1.0.0-rc.2'
@'
gateway: { mode: all }
controlPlane: { auth: { developmentBypass: true } }  # try-out only
all: { service: { type: ClusterIP } }
portal: { enabled: true, auth: { mode: Development } }  # try-out only
'@ | Set-Content -Encoding ascii gatewright-values.yaml
helm upgrade --install gatewright oci://ghcr.io/pgourlain/charts/gatewright --version $GW_VERSION --namespace gatewright --create-namespace -f gatewright-values.yaml
```

The [cluster quickstart](docs/user/quickstart-cluster.md) walks through every step: creating the local cluster, the four install options
(all-in-one or separate, with or without the portal), publishing a first API, and what to expose publicly or keep private.

### Develop on Gatewright itself

```bash
dotnet build Gatewright.slnx
dotnet test Gatewright.slnx
```

## Documentation

- Deploy on a local or real Kubernetes cluster from the published images (zsh and PowerShell): [docs/user/quickstart-cluster.md](docs/user/quickstart-cluster.md)
- Architecture and pods to deploy per mode: [docs/user/architecture.md](docs/user/architecture.md)
- All user documentation: [docs/user/README.md](docs/user/README.md); release procedure: [docs/release.md](docs/release.md)
- Architecture decisions: [docs/adr](docs/adr)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Security policy: [SECURITY.md](SECURITY.md)
- Governance: [GOVERNANCE.md](GOVERNANCE.md)

## License

[MIT](LICENSE). The name and logo are covered by [TRADEMARKS.md](TRADEMARKS.md).
