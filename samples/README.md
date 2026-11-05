# Samples

Each directory is self-contained. The container samples use one `compose.yaml` that works with **Docker**, **Podman** and **Colima**:

| Runtime | Command |
|---|---|
| Docker | `docker compose up --build` |
| Podman | `podman compose up --build` (Podman 4.7+ with the `podman-compose` or `docker-compose` provider) |
| Colima | `colima start`, then `docker compose up --build` (Colima provides the Docker engine) |

Run the commands from inside the sample directory. The images are built from the `Dockerfile` at the repository root.

| Sample | Shows |
|---|---|
| [01-quickstart](01-quickstart) | one gateway, JSON file configuration with hot reload |
| [02-control-and-data-plane](02-control-and-data-plane) | control plane + 2 data planes + web portal, publication, last known good config |
| [03-redis-shared-limits](03-redis-shared-limits) | rate limit shared by several gateways through Redis |
| [04-custom-policy](04-custom-policy) | your own C# policy in a customer distribution |
| [05-observability](05-observability) | OpenTelemetry, Prometheus and Grafana |
| [06-otel-gui](06-otel-gui) | otel-gui alone: traces, logs and metrics viewer, no collector |
| [07-products](07-products) | product catalogue: two products, plans, customers, subscriptions and API keys |
| [08-without-containers](08-without-containers) | `dotnet run` only, with the CLI |

Building blocks: [backend-orders](backend-orders) (sample backend), [custom-policy](custom-policy) (the policy project), [dev-config.json](dev-config.json).
For Kubernetes, see the Helm chart in `deploy/helm` (phase 13).
