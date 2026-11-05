// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>The last discovery state written by the leader (a single row).</summary>
public sealed class DiscoveryStateEntity
{
    /// <summary>The fixed identifier of the single row.</summary>
    public const int SingletonId = 1;

    /// <summary>Gets or sets the identifier.</summary>
    public int Id { get; set; } = SingletonId;

    /// <summary>Gets or sets the state as JSON.</summary>
    public string StateJson { get; set; } = "";

    /// <summary>Gets or sets the hash of the endpoints, to detect changes cheaply.</summary>
    public string EndpointsHash { get; set; } = "";

    /// <summary>Gets or sets when the state was written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
