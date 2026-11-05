// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>Everything discovery knows at a point in time.</summary>
/// <param name="Services">The discovered services.</param>
/// <param name="Endpoints">The ready addresses behind them.</param>
public sealed record DiscoveryState(IReadOnlyList<DiscoveredService> Services, ServiceEndpointSet Endpoints)
{
    /// <summary>Gets a state with nothing discovered.</summary>
    public static DiscoveryState Empty { get; } = new([], ServiceEndpointSet.Empty);
}
