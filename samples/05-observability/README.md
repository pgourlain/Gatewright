# 05 - Observability

Gateway exports OTLP to an OpenTelemetry Collector, which exposes metrics to Prometheus; Grafana shows the dashboard of
`deploy/observability/grafana-gatewright.json`.

```bash
docker compose up --build -d
for i in $(seq 1 200); do curl -s -o /dev/null http://localhost:8080/orders; done
```

- Grafana: http://localhost:3000 (anonymous admin, dashboard "Gatewright")
- Prometheus: http://localhost:9090 (`gatewright_requests_total`, `gatewright_request_duration_milliseconds_bucket`, `gatewright_rejections_total`)
- Traces and logs are printed by the collector: `docker compose logs otel-collector`

The 101st request within a minute is rejected with `429` and appears in `gatewright_rejections_total{cause="rate_limited"}`.
