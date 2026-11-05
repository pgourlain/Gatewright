// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>The complete, declarative configuration of a gateway.</summary>
public sealed record GatewayConfig
{
    /// <summary>Gets the configuration version.</summary>
    public long Version { get; init; }

    /// <summary>Gets the policies applied to every API (global scope).</summary>
    public PolicySet GlobalPolicies { get; init; } = PolicySet.Empty;

    /// <summary>Gets the policies applied to every API of this gateway (gateway scope).</summary>
    public PolicySet GatewayPolicies { get; init; } = PolicySet.Empty;

    /// <summary>Gets the shared named policies that can be referenced through <see cref="PolicyReference.Ref"/>.</summary>
    public IReadOnlyDictionary<string, PolicyReference> Policies { get; init; } = new Dictionary<string, PolicyReference>();

    /// <summary>Gets the products.</summary>
    public IReadOnlyList<ProductDefinition> Products { get; init; } = [];

    /// <summary>Gets the public hostnames: certificates, HTTPS redirection and HSTS.</summary>
    public IReadOnlyList<HostnameDefinition> Hostnames { get; init; } = [];

    /// <summary>Gets the subscriptions.</summary>
    public IReadOnlyList<SubscriptionDefinition> Subscriptions { get; init; } = [];

    /// <summary>Gets the APIs.</summary>
    public IReadOnlyList<ApiDefinition> Apis { get; init; } = [];
}
