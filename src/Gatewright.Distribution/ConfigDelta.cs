// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Distribution;

/// <summary>The changes between two configurations: entities added or replaced, and names removed. Matched by name (id for subscriptions).</summary>
public sealed record ConfigDelta
{
    /// <summary>Gets the APIs added or replaced.</summary>
    public List<ApiDefinition> ApisUpserted { get; init; } = [];

    /// <summary>Gets the names of removed APIs.</summary>
    public List<string> ApisRemoved { get; init; } = [];

    /// <summary>Gets the products added or replaced.</summary>
    public List<ProductDefinition> ProductsUpserted { get; init; } = [];

    /// <summary>Gets the names of removed products.</summary>
    public List<string> ProductsRemoved { get; init; } = [];

    /// <summary>Gets the subscriptions added or replaced.</summary>
    public List<SubscriptionDefinition> SubscriptionsUpserted { get; init; } = [];

    /// <summary>Gets the ids of removed subscriptions.</summary>
    public List<string> SubscriptionsRemoved { get; init; } = [];

    /// <summary>Gets the shared policies added or replaced.</summary>
    public Dictionary<string, PolicyReference> PoliciesUpserted { get; init; } = [];

    /// <summary>Gets the names of removed shared policies.</summary>
    public List<string> PoliciesRemoved { get; init; } = [];

    /// <summary>Gets the global policies when they changed.</summary>
    public PolicySet? GlobalPolicies { get; init; }

    /// <summary>Gets the gateway policies when they changed.</summary>
    public PolicySet? GatewayPolicies { get; init; }

    /// <summary>Gets a value indicating whether the delta changes nothing.</summary>
    public bool IsEmpty =>
        ApisUpserted.Count + ApisRemoved.Count + ProductsUpserted.Count + ProductsRemoved.Count + SubscriptionsUpserted.Count
        + SubscriptionsRemoved.Count + PoliciesUpserted.Count + PoliciesRemoved.Count == 0
        && GlobalPolicies is null && GatewayPolicies is null;
}
