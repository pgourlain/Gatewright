// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Options of the synchronization of Gateway API routes into the database (ADR 0017); same section as the watcher.</summary>
public sealed class GatewayApiSyncOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "GatewayApi";

    /// <summary>Gets or sets a value indicating whether the source is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the environments a synchronized change is published to. Empty: a revision is created and nothing is published.</summary>
    public List<string> PublishTo { get; set; } = [];

    /// <summary>Gets or sets how many consecutive reconciliations must miss a route before its API is deleted.</summary>
    public int DeletionGraceReconciliations { get; set; } = 3;

    /// <summary>Gets or sets the most APIs deleted by one reconciliation. Beyond it nothing is deleted and an error is logged.</summary>
    public int MaxDeletionsPerCycle { get; set; } = 5;

    /// <summary>Gets or sets a value indicating whether the outcome is written to the route status.</summary>
    public bool WriteStatus { get; set; }
}
