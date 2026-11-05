# The `gatewright` CLI

Install: `dotnet tool install -g Gatewright.Cli`.

```bash
gatewright login --server https://gatewright.example.com --token "$(az account get-access-token --resource api://gatewright-admin --query accessToken -o tsv)"
# or, in CI: GATEWRIGHT_SERVER and GATEWRIGHT_TOKEN environment variables (they win over the stored file)

gatewright api import orders-openapi.yaml --service shop/orders:80 --product starter [--dry-run] [--keep-missing]
gatewright publish --env dev                 # validates, then publishes the latest draft (or --revision N)
gatewright rollback --env dev --revision 3
gatewright nodes --env dev                   # which version each data plane applied

gatewright config export --env prod -o prod.json   # GitOps / promotion
gatewright config validate prod.json               # offline, same rules as the control plane (+ built-in policy schemas)
gatewright config import prod.json [--dry-run]
```

`api import` accepts a file, an `http(s)` URL or `-` (stdin), reads `x-gateway` (see [x-gateway](x-gateway.md)) and prints what was added, updated and removed.
Exit codes: `0` success, `1` failure (validation errors, server refusal, not authenticated), `2` invalid usage.
The token is stored in `~/.gatewright/config.json` readable only by you; prefer environment variables in CI.
