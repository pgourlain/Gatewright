# Declaring APIs

Gatewright never asks for YAML or CRDs. The source of truth is the control plane database; every route below ends up there as a **draft revision** that you validate and publish to an environment (`dev`, `staging`, `prod` by default). Nothing reaches the data plane before publication.

## 1. Web portal

Open the portal (see the [cluster quickstart](quickstart-cluster.md#7-optional-web-portal)), sign in with Entra ID and use:

- **APIs**: the catalog, with the *effective* policies of every operation after scope inheritance.
- **Import**: drop an OpenAPI document, preview the import report, apply.
- **Policy editor**: forms generated from each policy's JSON Schema; no JSON to hand-write.
- **Products & subscriptions**: plans, consumers, one-time API keys.
- **Revisions**: diff, publish, rollback. **Test console**: call an API and see the per-policy trace.

## 2. OpenAPI import

Any OpenAPI 3.0/3.1/3.2 document (JSON or YAML) can be imported. Gateway intent is carried by the optional [`x-gateway`](x-gateway.md) extension (rate limits, cache, auth, backend). Without it, the import creates plain routes you refine in the portal.

```bash
gatewright api import orders-openapi.yaml --service shop/orders:80 --product starter
```

Re-importing updates the API; an unchanged document creates no revision.

## 3. Code-first (.NET backends)

Reference `Gatewright.Exposure` in an ASP.NET Core backend and declare policies next to the endpoints. They are emitted as `x-gateway` in the native OpenAPI document:

```csharp
builder.Services.AddOpenApi(o => o.AddGatewayExposure());

app.MapGet("/orders/{id}", GetOrder)
   .WithRateLimit(100, TimeSpan.FromMinutes(1))
   .CacheOnGateway(TimeSpan.FromSeconds(30));
```

## 4. Kubernetes auto-discovery

Annotate a Service and the control plane (leader replica) discovers it:

| Annotation | Effect |
|---|---|
| `gatewright.io/expose: "true"` | The Service and its ready EndpointSlice addresses become available as a backend |
| `gatewright.io/openapi-path: /openapi/v1.json` | The OpenAPI document is fetched and imported as a draft; unchanged documents create no revision |
| `gatewright.io/base-path`, `gatewright.io/product` | Route prefix and product of the imported API |

Auto-publication is off unless `Kubernetes:AutoImport:PublishTo` lists environments.

## 5. CLI and GitOps

`gatewright config export --env prod -o prod.json` and `config import` move a whole environment as one JSON file; `config validate` runs the control plane's rules offline. See the [CLI reference](cli.md). Export then import yields the same configuration hash, which makes promotion between environments reproducible.

## Publishing

Whatever the entry point: validate → publish to an environment → data planes apply the snapshot atomically (typically in seconds) → `gatewright nodes --env <e>` shows the applied version per pod. `gatewright rollback --env <e> --revision <n>` reverts.
