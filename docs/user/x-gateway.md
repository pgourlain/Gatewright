# The `x-gateway` OpenAPI extension (version 1)

`x-gateway` lets an OpenAPI document tell Gatewright how to expose it. It is read by the OpenAPI importer (CLI `gatewright api import`, Admin API `POST /admin/v1/apis/import`,
the portal, Kubernetes auto-import) and generated for you by the code-first package `Gatewright.Exposure`. The version of the extension is declared at document level:
`x-gateway-version: 1`. A document with a higher version is refused with a clear message.

JSON Schema: [`docs/schemas/x-gateway-1.schema.json`](../schemas/x-gateway-1.schema.json).

## Document level

```yaml
openapi: 3.0.3
info: { title: Orders API, version: "1.0" }
x-gateway-version: 1
x-gateway:
  name: orders              # API name, default: slug of info.title
  basePath: /orders         # default: path of servers[0].url, else /<name>
  product: starter          # or products: [starter, gold]
  backend:                  # optional: usually chosen at import time
    service: shop/orders:80          # a discovered Kubernetes service...
    # destinations: [http://orders:8080]   # ...or static destinations
    timeout: "00:00:30"
    httpVersion: "2"
    healthCheck: { passive: { enabled: true } }
  policies:                 # policies of the API scope, by phase
    inbound:
      - type: jwt
        config: { issuers: [{ issuer: https://login.example.com, audiences: [api://orders], jwksUri: https://login.example.com/jwks }] }
  cors: { origins: ["https://app.example.com"] }   # shortcut, see below
```

Options given at import time (name, base path, backend, products) take precedence over the document.

## Operation level

```yaml
paths:
  /orders:
    get:
      operationId: listOrders
      x-gateway:
        rateLimit: { limit: 100, periodSeconds: 60 }
        cache: { durationSeconds: 30 }
    post:
      operationId: createOrder
      x-gateway:
        timeout: { seconds: 5 }
        policies:
          inbound:
            - { type: mockResponse, config: { status: 201 } }
```

Operations are named after `operationId` (sanitized), otherwise `<method>-<path>`; duplicates are renamed with a warning.

## Shortcuts

A key named after a built-in policy configures that policy directly. The value is the policy configuration.

| Key | Policy | Phase(s) |
|---|---|---|
| `jwt`, `apiKey`, `ipFilter`, `rateLimit`, `quota`, `cache`, `rewrite`, `mockResponse` | same name | inbound |
| `cors` | `cors` | inbound and outbound |
| `timeout`, `retry`, `circuitBreaker` | same name | backend |

Anything else goes under `policies` (`inbound`, `backend`, `outbound`, `onError`), each entry being `{ type, config, ref, inheritance }`
(`inheritance`: `inherit` (default), `override`, `remove`). Unknown `x-gateway` keys are ignored with a warning.

## Compatibility

Version 1 is stable: new keys may be added (older Gatewright versions warn and ignore them); a breaking change bumps `x-gateway-version`.
