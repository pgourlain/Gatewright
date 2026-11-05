# Writing a custom policy in C#

You extend Gatewright by building **your own distribution**: the standard gateway plus your policies, in your own image. The same image runs the control plane
(which validates configurations that use your policy types and lists them in the catalog) and the data planes (which execute them).

## 1. Create the host

```bash
dotnet new install Gatewright.Templates      # once
dotnet new gatewright-host -n Acme.Gateway
cd Acme.Gateway
```

Or by hand: a web project referencing the `Gatewright.Sdk` package with

```csharp
GatewayHost.CreateBuilder(args)
    .AddPolicy<FraudCheckPolicy>()
    .Build()
    .Run();
```

## 2. Write the policy

```csharp
public sealed record FraudCheckConfig
{
    [Description("Orders above this amount are rejected.")]
    public required decimal MaxAmount { get; init; }
    public IReadOnlyList<string> BlockedCountries { get; init; } = [];
}

[GatewayPolicy("acme.fraudCheck", Phases = PolicyPhase.Inbound)]
public sealed class FraudCheckPolicy : GatewayPolicy<FraudCheckConfig>
{
    protected override IPolicyInstance Create(FraudCheckConfig config, PolicyCompilationContext context)
    {
        // Runs once per snapshot: precompute everything here.
        var blocked = new HashSet<string>(config.BlockedCountries, StringComparer.OrdinalIgnoreCase);
        return PolicyInstance.From(request =>
            blocked.Contains(request.HttpContext.Request.Headers["x-country"].ToString())
                ? PolicyResult.Reject(403, "fraud_blocked_country", "Orders from this country are not accepted.")
                : PolicyResult.Continue);
    }
}
```

- The **JSON Schema** of the configuration is generated from `FraudCheckConfig`: `required` members are required, `[Description]` becomes the description,
  unknown properties are rejected. Invalid configurations are refused when published, with the JSON path of each error.
- `Create` runs **once per snapshot**. The returned instance runs for every request: keep it thread-safe and allocation free (no per-request LINQ, reflection or JSON).
- Return `PolicyResult.Continue`, `Reject(status, code, message)` or `Respond(response)`. Phases: `Inbound`, `Backend`, `Outbound`, `OnError`.
  A policy that wraps the backend call (timeouts, retries) implements `IBackendPolicyInstance`.
- Get services (HTTP clients, caches…) from `context.Services` in `Create`, never per request. Register them on `builder.Services`.

## 3. Test it

```csharp
await using var host = await GatewayTestHost.StartAsync(
    builder => builder.AddPolicy<FraudCheckPolicy>(),
    backend => new GatewayConfig
    {
        Apis = [GatewayTestHost.Api(backend, "orders", new PolicySet
        {
            Inbound = [GatewayTestHost.Policy("acme.fraudCheck", """{"maxAmount":1000}""")],
        })],
    });

var response = await host.Client.PostAsync("/orders/create", content);
host.Backend.Requests.ShouldBeEmpty();   // the request never reached the backend
```

`GatewayTestHost` (package `Gatewright.Testing`) starts the whole gateway in memory in front of a simulated backend that records requests; `UpdateConfigAsync`
changes the configuration while running. See `samples/custom-policy` and `tests/Gatewright.Samples.Tests` for a complete example.

## 4. Logs, traces and metrics from your policy

Everything goes through the standard .NET APIs and is exported with the gateway's own telemetry (OTLP when `Telemetry:Otlp:Endpoint` is set, see [observability](observability.md)).

- **Logs**: get a logger when the policy is compiled, `context.Services.GetRequiredService<ILoggerFactory>().CreateLogger<MyPolicy>()`, and keep it in the closure.
  Use cached `LoggerMessage` delegates on the request path: nothing is formatted unless the level is enabled. Never log credentials.
- **Traces**: `Activity.Current` is the span of the gateway request, so `SetTag` / `AddEvent` annotate it directly, with nothing to register.
  To create your own child spans, declare one static `ActivitySource` and register it, otherwise it is not exported:
  `host.Services.ConfigureOpenTelemetryTracerProvider(t => t.AddSource("Acme.Gateway"))`. `StartActivity` returns `null` (and costs almost nothing) when nothing listens.
- **Metrics**: declare static `Meter` and instruments, and register them: `host.Services.ConfigureOpenTelemetryMeterProvider(m => m.AddMeter("Acme.Gateway"))`.
  Keep tag values to a small fixed set (reasons, outcomes): one series is created per distinct combination.

`samples/custom-policy` shows all three (`FraudCheckPolicy.cs`, `FraudTelemetry.cs`) and `tests/Gatewright.Samples.Tests/FraudCheckTelemetryTests.cs` shows how to assert them.
Create the `ActivitySource` and `Meter` once (static), never per request; the usual rules for the hot path apply: no allocation or blocking call per request.

## 5. Ship it

Build your container image from your host project (`dotnet publish /t:PublishContainer`) and deploy it with the Helm chart by overriding `image.repository`.
Use the same image for the control plane and the data planes. Your policy appears in `GET /gw/policies` with its schema, ready for the portal and the CLI.

Compatibility: `Gatewright.Abstractions` and `Gatewright.Sdk` follow SemVer; deprecated members are marked `[Obsolete]` for a major version before removal.
