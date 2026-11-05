// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Core.Snapshots;

/// <summary>Security material carried by a snapshot: subscriptions and the hashes of their keys. Never plaintext secrets.</summary>
public sealed record SecurityData
{
    /// <summary>Gets an empty instance.</summary>
    public static SecurityData Empty { get; } = new();

    /// <summary>Gets the public hostnames with their certificate sources.</summary>
    public IReadOnlyList<HostnameDefinition> Hostnames { get; init; } = [];

    /// <summary>Gets the subscriptions.</summary>
    public IReadOnlyList<SubscriptionDefinition> Subscriptions { get; init; } = [];
}
