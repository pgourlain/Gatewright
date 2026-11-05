// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Snapshots;

/// <summary>The outcome of translating a configuration into a snapshot.</summary>
/// <param name="Snapshot">The snapshot, or <see langword="null"/> when there are errors.</param>
/// <param name="Errors">All validation and compilation errors.</param>
public sealed record SnapshotTranslationResult(ConfigSnapshot? Snapshot, IReadOnlyList<string> Errors)
{
    /// <summary>Gets a value indicating whether a snapshot was produced.</summary>
    public bool IsSuccess => Snapshot is not null;
}
