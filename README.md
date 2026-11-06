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

Requirements: .NET 10 SDK.

```bash
dotnet build Gatewright.slnx
dotnet test Gatewright.slnx
```

A runnable local setup (mode `all`, file configuration) arrives with phase 2 of the roadmap.

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
