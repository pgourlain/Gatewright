// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>A plan of a product (for example <c>free</c>, <c>premium</c>).</summary>
public sealed class PlanEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the owning product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the name, unique within the product.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets a description.</summary>
    public string? Description { get; set; }
}
