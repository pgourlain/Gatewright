// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>A product groups APIs and plans that consumers subscribe to.</summary>
public sealed class ProductEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether calls require a subscription.</summary>
    public bool RequiresSubscription { get; set; }

    /// <summary>Gets or sets the policies declared at product scope.</summary>
    public PolicySet Policies { get; set; } = PolicySet.Empty;

    /// <summary>Gets the APIs of the product.</summary>
    public List<ApiEntity> Apis { get; } = [];

    /// <summary>Gets the plans of the product.</summary>
    public List<PlanEntity> Plans { get; } = [];
}
