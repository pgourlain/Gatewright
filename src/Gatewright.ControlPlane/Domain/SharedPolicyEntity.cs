// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>A named policy configuration that APIs, products and operations can reference.</summary>
public sealed class SharedPolicyEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the policy type and configuration.</summary>
    public PolicyReference Definition { get; set; } = new();
}
