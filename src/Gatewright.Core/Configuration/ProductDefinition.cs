// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>A product groups APIs and carries policies shared by them (plans, quotas…).</summary>
public sealed record ProductDefinition
{
    /// <summary>Gets the unique product name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets a value indicating whether calls require a subscription.</summary>
    public bool RequiresSubscription { get; init; }

    /// <summary>Gets the policies declared at product scope.</summary>
    public PolicySet Policies { get; init; } = PolicySet.Empty;
}
