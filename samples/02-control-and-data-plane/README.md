# 02 - Control plane, data planes and portal

The production topology on one machine:

```
portal :8088 --> control plane :8081 (Admin API) / :8082 (gRPC)  <-- data plane 1 :8080
                        |                                         <-- data plane 2 :8090
                     SQLite volume
```

```bash
docker compose up --build -d     # or podman compose / colima + docker compose
./seed.sh                        # imports gateway-config.json and publishes it to "dev"

curl http://localhost:8080/orders          # data plane 1
curl http://localhost:8090/orders          # data plane 2
curl http://localhost:8081/admin/v1/nodes  # which version each data plane applied
```

Open the portal on http://localhost:8088 to edit APIs, add policies and publish from the browser.
Publishing reaches both data planes within about a second. Stop the control plane (`docker compose stop controlplane`):
the data planes keep serving their last known configuration.

`seed.sh` needs `curl` on the host. The same result with the CLI:
`gatewright login --server http://localhost:8081 --token dev && gatewright config import gateway-config.json && gatewright publish --env dev`.

**Not for production:** `DevelopmentBypass` and `Portal__Auth__Mode=Development` disable authentication. Use Entra ID / OIDC
(`ControlPlane__Auth__Authority`, `Portal__Auth__*`) and TLS or mTLS on the gRPC port.
