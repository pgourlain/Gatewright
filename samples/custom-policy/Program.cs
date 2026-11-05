// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Acme.Gateway;
using Gatewright.Sdk;

// The same host runs the control plane and the data planes (Gateway:Mode), so the control plane
// validates configurations that use acme.fraudCheck exactly like the data planes execute them.
var host = GatewayHost.CreateBuilder(args).AddPolicy<FraudCheckPolicy>();

// Export the policy's own spans and metrics next to the gateway's (OTLP when Telemetry:Otlp:Endpoint is set).
host.Services.AddFraudTelemetry();

host.Build().Run();
