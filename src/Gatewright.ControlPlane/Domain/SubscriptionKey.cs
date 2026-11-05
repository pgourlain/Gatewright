// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>A key of a subscription. Only its hash is stored; the plaintext is shown once at creation.</summary>
public sealed class SubscriptionKey
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the owning subscription identifier.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets the label (<c>primary</c> or <c>secondary</c>).</summary>
    public string Label { get; set; } = "";

    /// <summary>Gets or sets the key hash (lowercase hexadecimal).</summary>
    public string Hash { get; set; } = "";

    /// <summary>Gets or sets the creation time.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the revocation time, or <see langword="null"/> while the key is valid.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
