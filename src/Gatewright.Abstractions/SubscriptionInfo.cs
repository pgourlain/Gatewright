// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>The subscription a request was authenticated with (for example through an API key).</summary>
public sealed class SubscriptionInfo
{
    /// <summary>Initializes a new instance of the <see cref="SubscriptionInfo"/> class.</summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="productId">The identifier of the product the subscription belongs to.</param>
    /// <param name="plan">The plan name, or <see langword="null"/>.</param>
    /// <param name="isActive">Whether the subscription is active.</param>
    public SubscriptionInfo(string id, string productId, string? plan = null, bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        Id = id;
        ProductId = productId;
        Plan = plan;
        IsActive = isActive;
    }

    /// <summary>Gets the subscription identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the product identifier.</summary>
    public string ProductId { get; }

    /// <summary>Gets the plan name.</summary>
    public string? Plan { get; }

    /// <summary>Gets a value indicating whether the subscription is active.</summary>
    public bool IsActive { get; }
}
