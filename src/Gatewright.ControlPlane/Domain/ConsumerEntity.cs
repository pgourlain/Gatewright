// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>A consumer (application or team) that subscribes to products.</summary>
public sealed class ConsumerEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets a contact address.</summary>
    public string? Contact { get; set; }
}
