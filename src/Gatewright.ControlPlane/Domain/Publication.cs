// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>The event of a revision becoming the active configuration of an environment, with the snapshot distributed to data planes.</summary>
public sealed class Publication
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the environment identifier.</summary>
    public Guid EnvironmentId { get; set; }

    /// <summary>Gets or sets the published revision identifier.</summary>
    public Guid RevisionId { get; set; }

    /// <summary>Gets or sets the number of the publication within its environment; it is the configuration version seen by data planes.</summary>
    public long Number { get; set; }

    /// <summary>Gets or sets the kind of publication.</summary>
    public PublicationKind Kind { get; set; }

    /// <summary>Gets or sets the validated, versioned snapshot content (<c>GatewayConfig</c> JSON with <c>version</c> = <see cref="Number"/>).</summary>
    public string SnapshotJson { get; set; } = "";

    /// <summary>Gets or sets the content hash of the snapshot.</summary>
    public string SnapshotHash { get; set; } = "";

    /// <summary>Gets or sets who published.</summary>
    public string PublishedBy { get; set; } = "";

    /// <summary>Gets or sets when it was published.</summary>
    public DateTimeOffset PublishedAt { get; set; }
}
