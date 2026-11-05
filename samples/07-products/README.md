# 07 - Products, customers, subscriptions and API keys

A small catalogue managed through the Admin API (the same operations the portal and the CLI use):

| Product | Plan | APIs it gives access to |
|---|---|---|
| `starter` | `free` | `orders` (5 requests/minute per subscription) |
| `premium` | `business` | `orders` and `analytics` (100 requests/minute per subscription) |

| Customer | Subscription | Product |
|---|---|---|
| `acme` | `acme-starter` | `starter` |
| `globex` | `globex-premium` | `premium` |

Each API declares `apiKey` with the products allowed to call it (`"products": ["premium"]` for `analytics`), a `rateLimit` per subscription, and returns
`x-subscription`, `x-plan` and `x-product` response headers built from the identified subscription.

```bash
docker compose up --build -d      # or podman compose / colima + docker compose
./seed.sh                         # creates the catalogue, one key per customer, publishes; prints the keys (shown once)
```

```bash
curl -i  http://localhost:8080/orders                                            # 401: no key
curl -i -H "x-api-key: $ACME_KEY"   http://localhost:8080/orders                 # 200, x-plan: free (429 after 5 calls in a minute)
curl -i -H "x-api-key: $ACME_KEY"   http://localhost:8080/analytics              # 403 product_not_subscribed
curl -i -H "x-api-key: $GLOBEX_KEY" http://localhost:8080/analytics              # 200, x-plan: business
```

The web portal is on http://localhost:8088 (products, subscriptions and keys, revisions, test console). It calls the Admin API of the gateway.

Manage keys without downtime (Admin API on the same port, development mode):

```bash
curl -X POST   localhost:8080/admin/v1/subscriptions/acme-starter/keys/secondary          # issue a second key
curl -X POST   localhost:8080/admin/v1/subscriptions/acme-starter/keys/primary/rotate     # replace the primary key
curl -X DELETE localhost:8080/admin/v1/subscriptions/acme-starter/keys/primary            # revoke it
curl localhost:8080/admin/v1/subscriptions                                                  # keys are never returned, only their state
```

Notes:
- Run `seed.sh` once; `docker compose down -v` resets the catalogue (importing `catalog.json` replaces the draft).
- A *plan* is a label (and the `{subscription.plan}` variable usable in policies); it does not change limits by itself. Limits come from the policies of the API or product.
- Single port and `DevelopmentBypass` are for the demo. In production the Admin API is protected by Entra ID/OIDC and exposed separately (see sample 02).
