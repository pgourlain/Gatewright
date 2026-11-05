// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>An API exposed by the gateway (draft state of the configuration).</summary>
public sealed class ApiEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets a display name for portals.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the base path.</summary>
    public string BasePath { get; set; } = "";

    /// <summary>Gets or sets the API version.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the backend definition.</summary>
    public BackendDefinition Backend { get; set; } = new();

    /// <summary>Gets or sets the policies declared at API scope.</summary>
    public PolicySet Policies { get; set; } = PolicySet.Empty;

    /// <summary>Gets the operations.</summary>
    public List<OperationEntity> Operations { get; } = [];

    /// <summary>Gets the products the API belongs to.</summary>
    public List<ProductEntity> Products { get; } = [];
}
