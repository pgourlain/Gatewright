// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>Tells whether this replica is the one allowed to read the Kubernetes API and write the discovered state.</summary>
public interface ILeaderElection
{
    /// <summary>Gets a value indicating whether this replica currently holds the lease.</summary>
    bool IsLeader { get; }

    /// <summary>Waits until this replica becomes the leader.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A token cancelled when leadership is lost.</returns>
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken);
}
