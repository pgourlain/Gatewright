// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>A deployment target of published configurations (for example <c>dev</c>, <c>staging</c>, <c>prod</c>).</summary>
public sealed class GatewayEnvironment
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name, lowercase (<c>prod</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the identifier of the active publication, or <see langword="null"/> before the first one.</summary>
    public Guid? CurrentPublicationId { get; set; }
}
