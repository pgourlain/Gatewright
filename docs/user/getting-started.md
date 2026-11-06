# Getting started (local development)

Gatewright runs in a single process in mode `All`, reading its configuration from a JSON file that is reloaded when it changes.

On a cluster? See the [cluster quickstart](quickstart-cluster.md).

See [samples/README.md](../../samples/README.md) for ready-to-run examples (Docker, Podman or Colima compose, or plain `dotnet run`).

## Modes

See [architecture](architecture.md) for diagrams of each mode and the pods it adds next to your application.

| `Gateway:Mode` | Role |
|---|---|
| `All` | Control plane + data plane in one process (needs `Gateway:ConfigFile`) |
| `ControlPlane` | Control plane only (distribution to data planes arrives in a later phase) |
| `DataPlane` | Data plane only |

An unknown mode or a missing config file stops the process at startup with an explicit message.

## Configuration file

```json
{
  "version": 1,
  "apis": [
    { "name": "orders", "basePath": "/orders", "backend": { "destinations": ["http://localhost:5101"] } }
  ]
}
```

Durations in options use the .NET `hh:mm:ss` format (`00:00:30` is 30 seconds); a bare number such as `30000` means days and is refused at startup.
For example `Gateway:ConfigPollInterval` (default one second) sets how often the file, or the database in database mode, is checked for a new configuration.

A change to the file is validated, compiled and applied within about a second. An invalid file is logged and ignored: the previous configuration keeps serving traffic.

## Endpoints

- `GET /healthz`: liveness.
- `GET /readyz`: `200` once a valid configuration is applied, `503` before.
- `GET /gw/config`: applied version and hash. Requires `Authorization: Bearer <Gateway:DiagnosticsToken>`; disabled when the token is empty.

## Control plane (Admin API)

With `Gateway:Mode=ControlPlane`, or `Gateway:Mode=All` and `Gateway:ConfigSource=Database`, the process hosts the Admin API under `/admin/v1`
(OpenAPI document at `/admin/v1/openapi.json`) and a database (`ControlPlane:ConnectionString`).

- `ControlPlane:Provider=Sqlite` (default): no external dependency; the schema is created directly (`EnsureCreated`), no migration to run.
- `ControlPlane:Provider=Postgres`: the schema is created and upgraded with versioned EF Core migrations, applied automatically at startup.
  `ControlPlane:ConnectionString` is a standard Npgsql connection string, e.g. `Host=localhost;Database=gatewright;Username=gatewright;Password=gatewright`.

- Authentication: JWT bearer tokens from Entra ID or any OIDC provider (`ControlPlane:Auth:Authority`, `ControlPlane:Auth:Audience`).
  App roles `Admin`, `Publisher` (publish and roll back) and `Reader` come from the `roles` claim.
  For local development only, `ControlPlane:Auth:DevelopmentBypass=true` with `ASPNETCORE_ENVIRONMENT=Development` accepts every caller as an administrator.
- Every change creates a revision and an audit entry. `POST /admin/v1/environments/{env}/publications` validates and publishes a revision;
  `POST /admin/v1/environments/{env}/rollback` restores a previously published one. In `All` mode the gateway follows the active publication of `Gateway:Environment`.
- Subscription keys: `POST /admin/v1/subscriptions/{name}/keys/{primary|secondary}` returns the key once; only its HMAC-SHA256 hash is stored (`Gateway:ApiKeyPepper`).
  `.../rotate` replaces a key, `DELETE` revokes it.
- GitOps: `GET /admin/v1/export[?environment=prod]` returns a declarative document; `POST /admin/v1/import[?dryRun=true]` replaces the draft with one.

## Shared state with Redis

Set `Redis:ConnectionString` (from a secret store) to share state between gateway instances:

- `rateLimit` with `"shared": true` uses one global budget: each instance reserves batches of tokens from Redis and spends them locally
  (the global limit can be exceeded by at most one batch per instance). `"onStoreFailure"` chooses `failOpen` (default) or `failClosed` (503).
- `quota` counters become strict and shared.
- Key revocations made through the Admin API are announced over Redis pub/sub and applied by every gateway in seconds, without waiting for a new publication.

Redis calls have a short timeout (`Redis:CommandTimeoutMilliseconds`, 100 ms) and a circuit breaker; metrics are exposed under the `Gatewright.State.Redis` meter.
Without Redis the same features work with process-local counters.

## Separate control plane and data planes

- Control plane: `Gateway:Mode=ControlPlane`. It serves the Admin API and the gRPC distribution service (`Distribution:Tokens` lists the accepted service tokens;
  several allow a rotation without downtime). gRPC needs HTTP/2: expose it on a TLS port, or on a dedicated cleartext port with `Kestrel:Endpoints:<name>:Protocols=Http2`.
  For mutual TLS set `ClientCertificateMode` on the Kestrel endpoint.
- Data plane: `Gateway:Mode=DataPlane` with `Distribution:Endpoint` (control plane address), `Distribution:Token` and `Gateway:Environment`.
  It receives a full snapshot on connection (a delta when it is one version behind), then each publication within about a second.
  If the control plane is unreachable the node keeps serving its last known good configuration and reconnects with exponential backoff.
- `GET /admin/v1/nodes` lists the nodes and the version each applied; the data plane exposes the `gatewright.config.version_applied` gauge (meter `Gatewright.DataPlane`).

## Kubernetes discovery

With `Kubernetes:Enabled=true` the control plane (in-cluster credentials, or `Kubernetes:KubeConfig`) discovers Services annotated with
`gatewright.io/expose: "true"` (optional: `gatewright.io/openapi-path`, `gatewright.io/base-path`, `gatewright.io/product`).
Only the elected leader (a Kubernetes `Lease`, configurable in `Kubernetes:Lease*`) reads the Kubernetes API; replicas share the result through the database.

A backend references a discovered service with `"backend": { "service": "namespace/name:port" }`. Ready pod addresses are pushed to the data planes as they scale or restart,
without a new publication; pods that are not ready or are terminating are removed from rotation. Service backends get passive health checks by default; any backend can
set `healthCheck.active` / `healthCheck.passive`.

Required RBAC for the control plane: `get/list/watch` on `services` and `endpointslices.discovery.k8s.io`, and `get/create/update` on `leases.coordination.k8s.io`.
The data plane needs no Kubernetes permissions.

## TLS, hostnames and proxies

- Public TLS may be terminated upstream (Front Door, Application Gateway): set `Gateway:ForwardedHeaders:TrustedProxies` (IPs or CIDR ranges) so `X-Forwarded-For/Proto/Host` are honored
  only from them; client IPs used by `rateLimit` and `ipFilter` are then the real ones.
- To terminate TLS in Gatewright set `Gateway:Tls:HttpsPort` (and `HttpPort`). Declare hostnames in the configuration (or `PUT /admin/v1/hostnames/{name}`):
  `{ "name": "api.example.com", "certificate": { "file": { "certPath": "/tls/tls.crt", "keyPath": "/tls/tls.key" } }, "redirectHttpToHttps": true, "hsts": { "maxAgeSeconds": 31536000 } }`.
  `name` can be a wildcard (`*.example.com`) or `*` (default certificate). Mount a Kubernetes TLS secret as a volume: files are re-read every `Gateway:Tls:ReloadSeconds`
  (30 by default), so rotation by cert-manager needs no restart and drops no connection. Private keys never appear in the configuration or the database.
- Alternatively, reference the secret by name instead of mounting it: `"certificate": { "secret": "my-namespace/site-tls" }`. With Kubernetes discovery enabled (`Kubernetes:Enabled=true`),
  the control plane reads that secret directly (`Kubernetes:Secrets:PollSeconds`, 10 by default) and pushes the certificate and key to every data plane over the distribution channel;
  the material is held in memory only, never written to the database. A data plane without a mounted volume or direct Kubernetes access still gets the certificate this way.
- `backend.httpVersion: "2"` sends HTTP/2 to the backend (also over cleartext), which is what gRPC services need. WebSocket upgrades are proxied transparently.
- Shutdown: `/readyz` returns 503 as soon as the process is asked to stop; `Gateway:Shutdown:DrainDelaySeconds` keeps serving for that long so load balancers can deregister the node
  before in-flight requests are drained.

### Automatic OpenAPI import

A discovered service annotated `gatewright.io/openapi-path` has its OpenAPI document fetched by the leader (through cluster DNS, `Kubernetes:AutoImport:UrlTemplate`) and its API created or
updated in the **draft** configuration: backend = the service, `gatewright.io/base-path` and `gatewright.io/product` applied, `x-gateway` read from the document. An unchanged document creates nothing.
Nothing reaches production on its own: a human publishes, unless `Kubernetes:AutoImport:PublishTo` lists environments (for example `["dev"]`) to publish automatically.
