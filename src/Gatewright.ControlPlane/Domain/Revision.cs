// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>An immutable capture of the whole configuration at a point in time. Each change of the draft creates one.</summary>
public sealed class Revision
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the sequence number (1, 2, 3…).</summary>
    public long Number { get; set; }

    /// <summary>Gets or sets the configuration as JSON (<c>GatewayConfig</c>).</summary>
    public string ConfigJson { get; set; } = "";

    /// <summary>Gets or sets the content hash of the configuration.</summary>
    public string ConfigHash { get; set; } = "";

    /// <summary>Gets or sets what changed, in one line.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Gets or sets who made the change.</summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>Gets or sets when the revision was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
