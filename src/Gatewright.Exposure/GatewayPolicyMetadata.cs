// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Exposure;

/// <summary>Endpoint metadata carrying one gateway policy, turned into <c>x-gateway</c> by the OpenAPI transformer.</summary>
public sealed class GatewayPolicyMetadata
{
    /// <summary>Initializes a new instance of the <see cref="GatewayPolicyMetadata"/> class.</summary>
    /// <param name="type">The policy type (for example <c>rateLimit</c> or <c>acme.fraudCheck</c>).</param>
    /// <param name="configuration">The policy configuration; serialized to JSON.</param>
    /// <param name="phase">The phase (<c>inbound</c>, <c>backend</c>, <c>outbound</c>, <c>onError</c>). Built-in shortcuts ignore it.</param>
    public GatewayPolicyMetadata(string type, object? configuration, string phase = "inbound")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        Type = type;
        Configuration = configuration;
        Phase = phase;
    }

    /// <summary>Gets the policy type.</summary>
    public string Type { get; }

    /// <summary>Gets the policy configuration.</summary>
    public object? Configuration { get; }

    /// <summary>Gets the phase.</summary>
    public string Phase { get; }
}
