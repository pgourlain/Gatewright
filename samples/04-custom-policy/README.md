# 04 - Custom policy (distribution model)

You do not fork Gatewright: your own host project references the `Gatewright.Sdk` package and registers its policies with
`GatewayHost.CreateBuilder(args).AddPolicy<FraudCheckPolicy>()` (see `samples/custom-policy`). Use the same image for the control plane and the
data planes so that both know the policy schema.

```bash
docker compose up --build -d
curl -i -H 'x-order-amount: 50'   http://localhost:8080/orders     # 200
curl -i -H 'x-order-amount: 5000' http://localhost:8080/orders     # rejected: amount above maxAmount
curl -i -H 'x-country: XX'        http://localhost:8080/orders     # rejected: blocked country
curl -H 'Authorization: Bearer dev-diagnostics' http://localhost:8080/gw/policies   # lists acme.fraudCheck and its JSON Schema
```
