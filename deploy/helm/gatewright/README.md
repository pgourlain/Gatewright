# Gatewright Helm chart

Deploys Gatewright (API Management + YARP reverse proxy) to Kubernetes.

## Install

Published charts need no clone. Pick one source; every command below installs the same chart.

**Helm repository (GitHub Pages)**

```bash
helm repo add gatewright https://pgourlain.github.io/Gatewright
helm repo update
helm install gatewright gatewright/gatewright --namespace gatewright --create-namespace
```

**OCI registry (GHCR)**, no `repo add` needed:

```bash
helm install gatewright oci://ghcr.io/pgourlain/charts/gatewright --version <x.y.z> --namespace gatewright --create-namespace
```

OCI installs require `--version`; the registry has no "latest" lookup. Find versions with `helm search repo gatewright --versions` (repository) or on the
[GHCR package page](https://github.com/pgourlain?tab=packages&repo_name=Gatewright) (OCI).

**Release asset**: every GitHub Release carries `gatewright-<version>.tgz`:

```bash
helm install gatewright https://github.com/pgourlain/Gatewright/releases/download/v<x.y.z>/gatewright-<x.y.z>.tgz --namespace gatewright --create-namespace
```

**From a clone** (development, unreleased changes):

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright --create-namespace
```

### Useful commands

```bash
helm search repo gatewright --versions                       # list versions (add --devel to include -rc.N)
helm show values gatewright/gatewright --version <x.y.z>     # every setting; same with the oci:// reference
helm install gatewright gatewright/gatewright --version <x.y.z> --devel ...   # a prerelease
helm upgrade gatewright gatewright/gatewright --namespace gatewright --reuse-values --version <x.y.z>
helm pull gatewright/gatewright --version <x.y.z> --untar    # download and unpack the chart to read or patch it
helm template gatewright oci://ghcr.io/pgourlain/charts/gatewright --version <x.y.z> -f my-values.yaml   # render without installing
```

Pin `--version` in CI and GitOps: a chart version and its default image tag (`appVersion`) move together.

### GitOps

Flux (repository):

```yaml
apiVersion: source.toolkit.fluxcd.io/v1
kind: HelmRepository
metadata: { name: gatewright, namespace: flux-system }
spec: { url: https://pgourlain.github.io/Gatewright, interval: 1h }
---
apiVersion: helm.toolkit.fluxcd.io/v2
kind: HelmRelease
metadata: { name: gatewright, namespace: gatewright }
spec:
  interval: 10m
  chart:
    spec: { chart: gatewright, version: "x.y.z", sourceRef: { kind: HelmRepository, name: gatewright, namespace: flux-system } }
  values:
    gateway: { mode: separate }
```

Flux (OCI): `HelmRepository` with `type: oci` and `url: oci://ghcr.io/pgourlain/charts`.

Argo CD: an `Application` with `source.repoURL: https://pgourlain.github.io/Gatewright`, `chart: gatewright`, `targetRevision: x.y.z`; for OCI use
`repoURL: ghcr.io/pgourlain/charts` (no scheme), `chart: gatewright`, with the repository registered as type `helm` and `enableOCI: true`.

## Modes (`gateway.mode`)

- `all` (default): one Deployment runs the control plane and the data plane in a single process. Simple,
  single replica. Good for evaluation and small installations.
- `separate`: an independent `controlPlane` Deployment (Admin API + gRPC distribution, `ClusterIP`) and a
  `dataPlane` Deployment (the proxy, `LoadBalancer`, scaled by `dataPlane.replicaCount`). Use this to scale
  the proxy independently of the control plane (D3).

## Database and shared state

The default SQLite provider (`controlPlane.connectionString`, backed by a `PersistentVolumeClaim`) is not
safe for more than one control plane replica. Set `postgres.connectionString` to an external PostgreSQL
(e.g. Azure Database for PostgreSQL) to switch (`ControlPlane:Provider=Postgres`, versioned migrations
applied automatically) and scale the control plane. `redis.connectionString` enables the shared data plane
state (batched rate limits, quotas, revocations) against an external Redis (e.g. Azure Cache for Redis).

For dev/test, `postgres.dev.enabled`/`redis.dev.enabled` bundle a single, non-managed `postgres:17-alpine` /
`redis:7-alpine` in this same release instead — no external dependency needed to try everything out. Ignored
whenever the matching `connectionString` is set. Not a substitute for a managed database in any other
environment.

## Authentication

Without `controlPlane.auth.authority`/`controlPlane.auth.audience` (Entra ID or any OIDC provider), the
Admin API and the portal reject every caller. Set `controlPlane.auth.developmentBypass: true` only for a
non-production try-out (every caller becomes an administrator).

## Security defaults

Every pod runs as non-root with a read-only root filesystem (`securityContext`); `/tmp` and the ASP.NET Data
Protection key ring are `emptyDir` volumes. Data plane pods do not mount a Kubernetes service account token
(`automountServiceAccountToken: false`): only the control plane talks to the Kubernetes API, to discover
annotated Services and read the TLS secrets referenced by declared hostnames.

RBAC (`rbac.create`, on by default when `kubernetes.enabled`) grants the control plane's ServiceAccount a
`ClusterRole` (read `Services`, `EndpointSlices`; read Secrets, since the discovery API has no narrower scope
than cluster-wide — tighten with your own namespaced `Role` if every TLS secret lives in known namespaces)
and a namespaced `Role` for its leader-election `Lease`. `NetworkPolicy` resources (`networkPolicy.enabled`,
on by default) deny ingress to every port except each component's own; this needs a CNI that enforces
`NetworkPolicy` (Calico, Cilium, Azure CNI...) and is a silent no-op otherwise. `azureWorkloadIdentity.enabled`
labels the control plane pod(s) `azure.workload.identity/use: "true"`; also set
`serviceAccount.annotations["azure.workload.identity/client-id"]` and label the namespace
`azure.workload.identity/use: "true"` yourself.

## Customer distribution

Override `image.repository`/`image.tag` (and `portal.image.*`) to point at your own registry and image, built
from your own host project (`dotnet new gatewright-host`, phase 9) with the same chart.

## End-to-end test

`./scripts/k8s-e2e.sh` builds the images, installs the chart (`gateway.mode=all`) on a real cluster, deploys
the `samples/backend-orders` sample as a `gatewright.io/expose`-annotated Service, then exercises it: the
Admin API creates and publishes an API backed by that discovered Service, and the proxy is checked end to
end. Needs `kind`, `kubectl`, `helm` and Docker:

```bash
kind create cluster --name gatewright && ./scripts/k8s-e2e.sh
```

`KEEP_NAMESPACE=1` skips the cleanup, to inspect a failed run. Runs in CI on every push and pull request
(`.github/workflows/k8s-e2e.yml`).

See `values.yaml` for the full list of settings.
