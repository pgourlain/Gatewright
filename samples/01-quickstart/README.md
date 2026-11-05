# 01 - Quickstart

One gateway container (mode `all`) in front of the sample orders backend. The configuration is the JSON file `gateway.json`,
reloaded within a second when you save it - no restart.

```bash
docker compose up --build        # or: podman compose up --build   /   colima start && docker compose up --build
curl http://localhost:8080/orders
curl http://localhost:8080/readyz
```

Edit `gateway.json` (for example change `basePath` to `/shop`) and call the new path.

Stop with `docker compose down`.
