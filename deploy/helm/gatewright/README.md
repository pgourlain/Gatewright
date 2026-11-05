# Gatewright Helm chart

Deploys Gatewright (API Management + YARP reverse proxy) to Kubernetes.

```bash
helm install my-gatewright deploy/helm/gatewright --set controlPlane.auth.authority=https://login.example/tenant
```

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
