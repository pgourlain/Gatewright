# Observability

Gatewright emits OpenTelemetry traces, metrics and logs. Export is OTLP, enabled by `Telemetry:Otlp:Endpoint`
(or the standard `OTEL_EXPORTER_OTLP_ENDPOINT`). Without an endpoint nothing is exported; meters stay available in-process.

```json
{
  "Telemetry": {
    "ServiceName": "gatewright",
    "Otlp": { "Endpoint": "http://otel-collector:4317", "Protocol": "grpc" },
    "SubscriptionDimension": false,
    "AccessLog": { "Enabled": true, "SampleRate": 0.1, "QueueCapacity": 10000, "MaskedQueryParameters": ["tenant"] },
    "Usage": { "Enabled": true, "FlushSeconds": 60 }
  }
}
```

`Protocol` is `grpc` (default) or `http/protobuf` (also read from `OTEL_EXPORTER_OTLP_PROTOCOL`). With `http/protobuf`, give the base address
(for example `http://otel-gui:4318`): Gatewright appends `/v1/traces`, `/v1/metrics` and `/v1/logs`.

## Metrics

| Instrument | Type | Tags |
|---|---|---|
| `gatewright.requests` | counter | `api`, `operation`, `product`, `status_class` (+ `subscription` if enabled) |
| `gatewright.request.duration` | histogram (ms) | same |
| `gatewright.rejections` | counter | `api`, `cause` (policy error code: `invalid_api_key`, `rate_limited`, `quota_exceeded`…) |
| `gatewright.policy.duration` | histogram | policy, phase, result (meter `Gatewright.Policies`) |
| `gatewright.usage.requests` / `.errors` | counter | `api`, `operation`, `product`, `subscription` (when `Usage.Enabled`) |

`SubscriptionDimension` is off by default: one series per subscription can overwhelm a metrics backend.
Use the usage metrics (flushed in batches) when you need per-subscription numbers.

Native YARP and Kestrel metrics are exported too (`Yarp.ReverseProxy`, `Microsoft.AspNetCore.Server.Kestrel`).

## Access logs

Written asynchronously: the request only enqueues an entry into a bounded queue; when it is full, entries are dropped and counted
(never blocking traffic). `SampleRate` applies to successful requests; responses with status 400 or above are always logged.
Headers are never logged. Query values named `key`, `api_key`, `token`, `access_token`, `id_token`, `refresh_token`, `code`, `password`,
`secret`, `client_secret`, `sig`, `signature` (and `MaskedQueryParameters`) are replaced with `***`.

## Usage analytics

Usage is aggregated in memory per API, operation, product and subscription, and sent every `FlushSeconds` to every registered `IUsageSink`.
The built-in sink publishes OTLP metrics. Register your own sink (for example Event Hubs or Kafka) with
`builder.Services.AddSingleton<IUsageSink, MySink>()`. A failing sink never affects traffic or other sinks.

## Dashboards and alerts

See `deploy/observability/`: a Grafana dashboard (`grafana-gatewright.json`), Azure Monitor KQL queries (`azure-monitor.kql`)
and Prometheus alert rules (`alerts.yaml`).
