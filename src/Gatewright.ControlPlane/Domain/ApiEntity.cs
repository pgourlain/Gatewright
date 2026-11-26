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

    /// <summary>Gets or sets the host names the API answers to (empty: any host).</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Gets or sets the kind of source that owns the API (<see cref="ApiSource.GatewayApi"/>), or <see langword="null"/> for an API declared by hand.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the reference of the source object (<c>namespace/name</c> of the route).</summary>
    public string? SourceRef { get; set; }

    /// <summary>Gets or sets the uid of the source object, for information.</summary>
    public string? SourceUid { get; set; }

    /// <summary>Gets or sets the product requested by the source object, resolved when the configuration is exported.</summary>
    public string? SourceProduct { get; set; }

    /// <summary>Gets or sets the <c>metadata.generation</c> of the source object last synchronized, to skip unchanged ones.</summary>
    public long? SourceGeneration { get; set; }

    /// <summary>Gets or sets the number of consecutive reconciliations that did not find the source object (deletion grace).</summary>
    public int SourceMissingCount { get; set; }

    /// <summary>Gets the operations.</summary>
    public List<OperationEntity> Operations { get; } = [];

    /// <summary>Gets the products the API belongs to.</summary>
    public List<ProductEntity> Products { get; } = [];
}
