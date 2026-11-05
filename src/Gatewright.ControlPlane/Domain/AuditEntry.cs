// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>Who did what and when.</summary>
public sealed class AuditEntry
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets when the action happened.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Gets or sets who did it.</summary>
    public string Actor { get; set; } = "";

    /// <summary>Gets or sets the action (for example <c>api.create</c>, <c>publish</c>).</summary>
    public string Action { get; set; } = "";

    /// <summary>Gets or sets the kind of object affected.</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Gets or sets the identifier or name of the object affected.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>Gets or sets a short description or a JSON diff.</summary>
    public string Details { get; set; } = "";
}
