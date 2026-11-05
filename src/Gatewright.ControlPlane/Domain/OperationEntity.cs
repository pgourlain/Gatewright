// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>An operation (method + path) of an API.</summary>
public sealed class OperationEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the owning API identifier.</summary>
    public Guid ApiId { get; set; }

    /// <summary>Gets or sets the name, unique within the API.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the HTTP method.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Gets or sets the path template relative to the API base path.</summary>
    public string Path { get; set; } = "/";

    /// <summary>Gets or sets the policies declared at operation scope.</summary>
    public PolicySet Policies { get; set; } = PolicySet.Empty;
}
