# Quickstart: deploy Gatewright on a Kubernetes cluster

Copy and paste each block in order. Every variant of the install (two modes × three database setups) is a single `helm install` command; pick one.

| | SQLite (default) | PostgreSQL bundled (dev/test) | PostgreSQL external (production) |
|---|---|---|---|
| **Mode `all`** (one pod) | [1a](#1a-all--sqlite) | [1b](#1b-all--bundled-postgresql) | [1c](#1c-all--external-postgresql) |
| **Mode `separate`** (control plane + data planes) | [2a](#2a-separate--sqlite) | [2b](#2b-separate--bundled-postgresql) | [2c](#2c-separate--external-postgresql) |

Not sure which one? Start with **1a** to try Gatewright, then move to **2c** for a real installation. See [architecture](architecture.md) for what each mode adds to your cluster.

> **These commands install a try-out configuration.** The Admin API accepts every caller as an administrator (`controlPlane.auth.developmentBypass=true`) and nothing is exposed outside the cluster. Before exposing it, read [Going to production](#going-to-production).

## 0. Prerequisites

You need `kubectl`, `helm` and a cluster. Use [kind](https://kind.sigs.k8s.io/) if you have none (Docker required):

```bash
kind create cluster --name gatewright
kubectl config use-context kind-gatewright
```

## 1. Get the chart and the image

The chart is in this repository:

```bash
git clone https://github.com/pgourlain/Gatewright.git
cd Gatewright
```

Then choose **one** way to get the image.

### Option A: use the published image (nothing to build)

```bash
export IMAGE_ARGS="--set image.repository=ghcr.io/pgourlain/gatewright-host --set image.tag=edge"
```

`edge` follows the latest build of `main`. To pin a version, use a commit tag (`ghcr.io/pgourlain/gatewright-host:<short-sha>`). The web portal image is `ghcr.io/pgourlain/gatewright-portal`.

### Option B: build the image yourself

Needs the .NET 11 SDK and Docker:

```bash
dotnet publish src/Gatewright.Host -c Release -t:PublishContainer -p:ContainerImageTag=quickstart -p:MinVerSkip=true
```

Make the image available to the cluster. **On kind:**

```bash
kind load docker-image gatewright-host:quickstart --name gatewright
export IMAGE_ARGS="--set image.repository=gatewright-host --set image.tag=quickstart --set image.pullPolicy=Never"
```

**On any other cluster** (AKS, EKS, GKE, k3s…), push it to a registry the cluster can pull from, replacing `registry.example.com/gatewright` with yours:

```bash
docker tag gatewright-host:quickstart registry.example.com/gatewright/gatewright-host:quickstart
docker push registry.example.com/gatewright/gatewright-host:quickstart
export IMAGE_ARGS="--set image.repository=registry.example.com/gatewright/gatewright-host --set image.tag=quickstart"
```

If the registry is private, create a pull secret and add `--set imagePullSecrets[0].name=<secret>` to `IMAGE_ARGS`.

Create the namespace:

```bash
kubectl create namespace gatewright
```

## 2. Install (pick one)

All variants use the release name `gatewright` in the namespace `gatewright`. Run **only one**.

### 1a. `all` + SQLite

One pod, no external dependency. The database is a SQLite file on a 1 GiB `PersistentVolumeClaim`. Single replica only.

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=all \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP
```

### 1b. `all` + bundled PostgreSQL

Same pod, but the database is a PostgreSQL installed by the chart. **Dev and test only**: a single non-managed `postgres:17-alpine`. Versioned migrations are applied automatically at startup.

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=all \
  --set postgres.dev.enabled=true \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP
```

### 1c. `all` + external PostgreSQL

Same pod, with your own managed PostgreSQL (Azure Database for PostgreSQL, RDS, Cloud SQL, your own…). Create an empty database and a user that owns it first, then:

```bash
export PG_CONN='Host=my-postgres.example.com;Database=gatewright;Username=gatewright;Password=CHANGE_ME;SSL Mode=Require'

helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=all \
  --set-string postgres.connectionString="$PG_CONN" \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP
```

`PG_CONN` is a standard [Npgsql connection string](https://www.npgsql.org/doc/connection-string-parameters.html). The chart stores it in a Kubernetes Secret, never in a ConfigMap.

### 2a. `separate` + SQLite

A control plane (Admin API, one pod, SQLite on a `PersistentVolumeClaim`) and two data plane pods (the proxy). Data planes scale independently and keep serving if the control plane goes down.

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=separate \
  --set controlPlane.auth.developmentBypass=true \
  --set controlPlane.service.type=ClusterIP \
  --set dataPlane.service.type=ClusterIP
```

### 2b. `separate` + bundled PostgreSQL

Same as 2a with a bundled PostgreSQL (dev and test only). With PostgreSQL the chart runs **two** control plane pods; one is elected leader through a Kubernetes `Lease`.

```bash
helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=separate \
  --set postgres.dev.enabled=true \
  --set controlPlane.auth.developmentBypass=true \
  --set controlPlane.service.type=ClusterIP \
  --set dataPlane.service.type=ClusterIP
```

### 2c. `separate` + external PostgreSQL

The recommended production shape. With PostgreSQL the chart runs two control plane pods (leader elected through a Kubernetes `Lease`) next to the data planes.

```bash
export PG_CONN='Host=my-postgres.example.com;Database=gatewright;Username=gatewright;Password=CHANGE_ME;SSL Mode=Require'

helm install gatewright deploy/helm/gatewright --namespace gatewright $IMAGE_ARGS \
  --set gateway.mode=separate \
  --set-string postgres.connectionString="$PG_CONN" \
  --set controlPlane.auth.developmentBypass=true \
  --set controlPlane.service.type=ClusterIP \
  --set dataPlane.service.type=ClusterIP
```

## 3. Wait until it is ready

**Mode `all`:**

```bash
kubectl --namespace gatewright wait --for=condition=ready pod -l app.kubernetes.io/component=all --timeout=180s
```

**Mode `separate`:**

```bash
kubectl --namespace gatewright wait --for=condition=ready pod -l app.kubernetes.io/component=controlplane --timeout=180s
```

The data plane pods stay *not ready* (`/readyz` returns 503) until you publish a first configuration in the next step. That is expected: a data plane never serves traffic without a valid configuration.

## 4. Deploy a sample backend and publish your first API

A tiny echo backend to proxy to:

```bash
kubectl --namespace gatewright create deployment echo --image=hashicorp/http-echo:1.0 -- /http-echo -listen=:8080 -text=hello-from-backend
kubectl --namespace gatewright expose deployment echo --port=80 --target-port=8080
kubectl --namespace gatewright rollout status deployment/echo --timeout=120s
```

Open the Admin API (leave this terminal running, use another one for the next commands).

**Mode `all`:**

```bash
kubectl --namespace gatewright port-forward svc/gatewright-all 8080:8080
```

**Mode `separate`:**

```bash
kubectl --namespace gatewright port-forward svc/gatewright-controlplane 8080:8081
```

In another terminal, declare the API and publish it to the `dev` environment:

```bash
curl -sf -X POST http://localhost:8080/admin/v1/apis -H 'content-type: application/json' \
  -d '{"name":"echo","basePath":"/echo","backend":{"destinations":["http://echo.gatewright.svc.cluster.local"]}}'

curl -sf -X POST http://localhost:8080/admin/v1/environments/dev/publications -H 'content-type: application/json' -d '{}'
```

## 5. Call it through the gateway

**Mode `all`** (the same port serves the Admin API and the proxy):

```bash
curl -i http://localhost:8080/echo
```

**Mode `separate`:** stop the previous port-forward (Ctrl+C), then reach the data plane:

```bash
kubectl --namespace gatewright port-forward svc/gatewright-dataplane 8080:8080
```

```bash
curl -i http://localhost:8080/echo
```

You should see `200` and `hello-from-backend`. In `separate` mode, check that every data plane pod applied the configuration:

```bash
kubectl --namespace gatewright get pods -l app.kubernetes.io/component=dataplane
```

All pods are `1/1 Ready`.

## 6. Optional: shared rate limits with Redis

Rate limits and quotas are per-pod without Redis. For a shared budget add **one** of these to your `helm install` (or run `helm upgrade --reuse-values …` afterwards):

```bash
# dev/test: a single non-persistent Redis installed by the chart
--set redis.dev.enabled=true

# production: your own managed Redis
--set-string redis.connectionString='my-redis.example.com:6380,password=CHANGE_ME,ssl=true'
```

Details in [getting-started](getting-started.md#shared-state-with-redis).

## 7. Optional: web portal

With the published image (Option A):

```bash
helm upgrade gatewright deploy/helm/gatewright --namespace gatewright --reuse-values \
  --set portal.enabled=true \
  --set portal.image.repository=ghcr.io/pgourlain/gatewright-portal --set portal.image.tag=edge
```

With your own build (Option B), build and load the portal image first, then upgrade with `portal.image.repository=gatewright-portal` and `portal.image.tag=quickstart`:

```bash
dotnet publish src/Gatewright.Portal -c Release -t:PublishContainer -p:ContainerImageTag=quickstart -p:MinVerSkip=true
kind load docker-image gatewright-portal:quickstart --name gatewright   # kind only
```

## Going to production

Before exposing Gatewright outside the cluster:

1. **Authenticate the Admin API.** Replace `controlPlane.auth.developmentBypass=true` with your OIDC provider (Entra ID or any other):
   `--set controlPlane.auth.authority=https://login.microsoftonline.com/<tenant>/v2.0 --set controlPlane.auth.audience=<api-client-id>`.
   Roles `Admin`, `Publisher` and `Reader` come from the `roles` claim.
2. **Expose the data plane.** Remove `--set dataPlane.service.type=ClusterIP` (or `all.service.type=ClusterIP`) to get the default `LoadBalancer`, or put your own Ingress/Front Door in front, setting `gateway.extraEnv.Gateway__ForwardedHeaders__TrustedProxies__0` to its address.
3. **Keep the Admin API private.** In `separate` mode keep `controlPlane.service.type=ClusterIP`. In `all` mode the Admin API shares the proxy port: prefer `separate` for anything public.
4. **Use an external PostgreSQL** (variants 1c / 2c), never the bundled one or SQLite, and keep the connection string in a secret store rather than shell history.
5. **Pin the image**: a commit tag from the published images, or your own tag from your own registry (step 1), never `edge` or `quickstart`.
6. **Size the data plane**: `--set dataPlane.replicaCount=3`, or `--set dataPlane.autoscaling.enabled=true`. See the chart's [`values.yaml`](../../deploy/helm/gatewright/values.yaml) for every setting, and [observability](observability.md) to monitor it.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Pod stuck in `ImagePullBackOff` or `ErrImageNeverPull` | The cluster cannot see the image. On kind re-run `kind load docker-image …`; elsewhere check the registry path and `imagePullSecrets`. |
| `curl` to the Admin API returns `401` or `403` | No OIDC authority is set and `developmentBypass` is off. Set one of them (step 2). |
| `helm install` fails with `cannot re-use a name that is still in use` | Run `helm uninstall gatewright --namespace gatewright` first. |
| Data plane pods never become ready (`separate`) | Nothing is published yet: run the publication in step 4. |
| `curl http://localhost:8080/echo` returns `404` | The publication did not happen, or you are still port-forwarded to the control plane (in `separate` mode, switch to the data plane in step 5). |
| Control plane crash-loops with a PostgreSQL error | Check the connection string and that the database exists and the user can create tables: `kubectl --namespace gatewright logs deploy/gatewright-controlplane` (or `deploy/gatewright-all`). |

## Uninstall

```bash
helm uninstall gatewright --namespace gatewright
kubectl delete namespace gatewright
```

The SQLite `PersistentVolumeClaim` and the bundled PostgreSQL volume are deleted with the namespace. An external PostgreSQL is left untouched.
