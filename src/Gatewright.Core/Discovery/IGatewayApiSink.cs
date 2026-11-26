// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>Receives the routes read from the cluster and brings the database in line with them (implemented by the control plane).</summary>
public interface IGatewayApiSink
{
    /// <summary>Reconciles the database with the routes of the cluster.</summary>
    /// <param name="state">The routes, from a complete and successful listing.</param>
    /// <param name="cancellationToken">Cancels the reconciliation.</param>
    /// <returns>One result per route.</returns>
    Task<IReadOnlyList<GatewayApiRouteResult>> ApplyAsync(GatewayApiState state, CancellationToken cancellationToken);
}
