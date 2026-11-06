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

Deploy Gatewright on a Kubernetes cluster (try-out configuration, SQLite, one pod). The chart is in this repository:

```bash
git clone https://github.com/pgourlain/Gatewright.git
cd Gatewright
kubectl create namespace gatewright
```

### Option A: use the published image (nothing to build)

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright \
  --set image.repository=ghcr.io/pgourlain/gatewright-host --set image.tag=edge \
  --set gateway.mode=all \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP
```

`edge` follows the latest build of `main`; use a commit tag (`ghcr.io/pgourlain/gatewright-host:<short-sha>`) to pin it.

### Option B: build the image yourself

Requires the .NET 10 SDK and Docker. Example on [kind](https://kind.sigs.k8s.io/):

```bash
dotnet publish src/Gatewright.Host -c Release -t:PublishContainer -p:ContainerImageTag=local -p:MinVerSkip=true
kind load docker-image gatewright-host:local --name gatewright
helm install gatewright deploy/helm/gatewright --namespace gatewright \
  --set image.repository=gatewright-host --set image.tag=local --set image.pullPolicy=Never \
  --set gateway.mode=all \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP
```

On another cluster, push the image to your own registry and set `image.repository`/`image.tag` accordingly.

Then publish your first API and call it: see the [cluster quickstart](docs/user/quickstart-cluster.md), which also covers the `separate` mode and PostgreSQL.

### Develop on Gatewright itself

```bash
dotnet build Gatewright.slnx
dotnet test Gatewright.slnx
```

## Documentation

- Deploy on a Kubernetes cluster, copy-paste, every mode and database: [docs/user/quickstart-cluster.md](docs/user/quickstart-cluster.md)
- Architecture and pods to deploy per mode: [docs/user/architecture.md](docs/user/architecture.md)
- Architecture decisions: [docs/adr](docs/adr)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Security policy: [SECURITY.md](SECURITY.md)
- Governance: [GOVERNANCE.md](GOVERNANCE.md)

## License

[MIT](LICENSE). The name and logo are covered by [TRADEMARKS.md](TRADEMARKS.md).
