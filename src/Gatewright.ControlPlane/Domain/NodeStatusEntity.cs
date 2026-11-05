// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>What the control plane knows about a data plane node: which configuration version it applied.</summary>
public sealed class NodeStatusEntity
{
    /// <summary>Gets or sets the node identifier (the primary key).</summary>
    public string NodeId { get; set; } = "";

    /// <summary>Gets or sets the environment the node follows.</summary>
    public string Environment { get; set; } = "";

    /// <summary>Gets or sets the last version the node applied successfully.</summary>
    public long AppliedVersion { get; set; }

    /// <summary>Gets or sets the hash of the configuration the node applied.</summary>
    public string AppliedHash { get; set; } = "";

    /// <summary>Gets or sets the last error the node reported when refusing a version.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets a value indicating whether the node currently has an open stream.</summary>
    public bool Connected { get; set; }

    /// <summary>Gets or sets when the node last connected.</summary>
    public DateTimeOffset ConnectedAt { get; set; }

    /// <summary>Gets or sets when the control plane last heard from the node.</summary>
    public DateTimeOffset LastSeen { get; set; }
}
