using Acme.Gateway;
using Gatewright.Sdk;

// The same host runs the control plane and the data planes (Gateway:Mode), so the control plane
// validates configurations that use your policies exactly like the data planes execute them.
GatewayHost.CreateBuilder(args)
    .AddPolicy<SamplePolicy>()
    .Build()
    .Run();
