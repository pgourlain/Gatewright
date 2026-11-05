# 06 - Observability with otel-gui

[otel-gui](https://github.com/metafab/otel-gui) (MIT) is a lightweight OpenTelemetry viewer for **traces, logs and metrics**: in memory, no database,
nothing else to run. The gateway exports straight to it over OTLP/HTTP (`Telemetry__Otlp__Protocol: http/protobuf`), no collector in between.
For long-term metrics and dashboards see [05-observability](../05-observability) (OpenTelemetry Collector, Prometheus, Grafana).

```bash
docker compose up --build -d     # or podman compose / colima + docker compose
./seed.sh                        # publishes the orders API (catalog.json) to the gateway
for i in $(seq 1 100); do curl -s -o /dev/null http://localhost:8080/orders; done
```

- Web portal: http://localhost:8088 (the gateway runs in database mode so that the portal can manage it: edit an API, publish, send a request from the test console, then look at the trace)
- otel-gui: http://localhost:4318 - trace waterfall (gateway, proxy and backend call spans), logs correlated to traces by trace id, and metrics (`gatewright.requests`, `gatewright.request.duration`, `gatewright.rejections`, usage counters)

otel-gui keeps the last 1000 traces/logs in memory (`OTEL_GUI_MAX_TRACES`, `OTEL_GUI_MAX_LOGS`): it is a development tool, not a backend to keep.
The image tag is `latest`; pin a version for reproducible demos.
