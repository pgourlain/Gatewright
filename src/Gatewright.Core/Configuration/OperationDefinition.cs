// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>One operation (method + path) of an API.</summary>
public sealed record OperationDefinition
{
    /// <summary>Gets the operation name, unique within its API.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>Gets the path template relative to the API base path.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the policies declared at operation scope.</summary>
    public PolicySet Policies { get; init; } = PolicySet.Empty;
}
