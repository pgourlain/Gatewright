// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>
/// Where the leader writes what discovery found so that every control plane replica can serve it (to data planes and to the Admin API).
/// Kubernetes is only read by the leader.
/// </summary>
public interface IDiscoveryStore
{
    /// <summary>Saves the state. Does nothing when it equals the stored one.</summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the state is stored.</returns>
    Task SaveAsync(DiscoveryState state, CancellationToken cancellationToken);

    /// <summary>Loads the last saved state.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state, or an empty one.</returns>
    Task<DiscoveryState> LoadAsync(CancellationToken cancellationToken);
}
