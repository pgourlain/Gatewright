// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Snapshots;

namespace Gatewright.Core.Distribution;

/// <summary>Carries snapshots from the control plane side to the data plane side.</summary>
public interface IConfigDistributor
{
    /// <summary>Publishes a snapshot. Only the latest unconsumed snapshot matters.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the snapshot is handed over.</returns>
    ValueTask PublishAsync(ConfigSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Streams snapshots as they are published.</summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>The snapshots.</returns>
    IAsyncEnumerable<ConfigSnapshot> SubscribeAsync(CancellationToken cancellationToken);
}
