# Custom policy sample: `acme.fraudCheck`

A customer distribution of Gatewright: the standard gateway plus one custom policy written in C#.

- `FraudCheckPolicy.cs`: the policy. Its configuration record is turned into a JSON Schema automatically.
- `Program.cs`: the host. `GatewayHost.CreateBuilder(args).AddPolicy<FraudCheckPolicy>()`.
- `FraudTelemetry.cs`: the policy's own span source, counter and their registration; the policy also writes a log through `ILogger` (see [extending](../../docs/user/extending.md#4-logs-traces-and-metrics-from-your-policy)).
- Tests: `tests/Gatewright.Samples.Tests` start the full gateway in memory with `GatewayTestHost` and send real requests.

Use it in a configuration:

```json
{ "type": "acme.fraudCheck", "config": { "maxAmount": 1000, "blockedCountries": ["XX"] } }
```

Run it like the standard host, for example:

```bash
dotnet run --project samples/custom-policy -- --Gateway:Mode=all --Gateway:ConfigFile=$PWD/samples/dev-config.json --urls http://localhost:8080
curl -H "Authorization: Bearer ..." http://localhost:8080/policies   # needs Gateway:DiagnosticsToken: lists acme.fraudCheck with its schema
```
