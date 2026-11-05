// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>An API exposed by the gateway.</summary>
public sealed record ApiDefinition
{
    /// <summary>Gets the unique API name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the base path under which the API is exposed.</summary>
    public required string BasePath { get; init; }

    /// <summary>Gets the API version.</summary>
    public string? Version { get; init; }

    /// <summary>Gets the backend.</summary>
    public required BackendDefinition Backend { get; init; }

    /// <summary>Gets the names of the products the API belongs to.</summary>
    public IReadOnlyList<string> Products { get; init; } = [];

    /// <summary>Gets the operations.</summary>
    public IReadOnlyList<OperationDefinition> Operations { get; init; } = [];

    /// <summary>Gets the policies declared at API scope.</summary>
    public PolicySet Policies { get; init; } = PolicySet.Empty;
}
