// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;

namespace Gatewright.IntegrationTests;

internal sealed class RejectPolicy : IGatewayPolicy
{
    public string Type => "reject";

    public PolicyPhase Phases => PolicyPhase.Inbound;

    public JsonElement ConfigSchema { get; } = JsonElement.Parse("""{"type":"object","properties":{"status":{"type":"integer"}}}""");

    public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context)
    {
        var status = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("status", out var s) ? s.GetInt32() : 403;
        return new Instance(status);
    }

    private sealed class Instance(int status) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) =>
            new(PolicyResult.Reject(status, "rejected", "Rejected by test policy."));
    }
}

internal sealed class AddHeaderPolicy : IGatewayPolicy
{
    public string Type => "addHeader";

    public PolicyPhase Phases => PolicyPhase.Outbound;

    public JsonElement ConfigSchema { get; } = JsonElement.Parse("""{"type":"object","required":["name","value"]}""");

    public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context) =>
        new Instance(config.GetProperty("name").GetString()!, config.GetProperty("value").GetString()!);

    private sealed class Instance(string name, string value) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            context.HttpContext.Response.Headers[name] = value;
            return new(PolicyResult.Continue);
        }
    }
}
