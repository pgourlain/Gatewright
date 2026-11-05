// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>A subscription of a consumer to a product. Keys are stored only as hashes (see <c>ApiKeyHasher</c>).</summary>
public sealed record SubscriptionDefinition
{
    /// <summary>Gets the unique subscription identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the name of the product subscribed to.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the plan name.</summary>
    public string? Plan { get; init; }

    /// <summary>Gets a value indicating whether the subscription is active.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Gets the hashes of the keys (primary, secondary…) that identify this subscription.</summary>
    public IReadOnlyList<string> KeyHashes { get; init; } = [];
}
