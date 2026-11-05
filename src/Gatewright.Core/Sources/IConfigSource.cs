// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Core.Sources;

/// <summary>A source of gateway configurations that reports changes.</summary>
public interface IConfigSource
{
    /// <summary>Yields the current configuration, then every new valid version when it changes.</summary>
    /// <param name="cancellationToken">Stops the watch.</param>
    /// <returns>A stream of configurations.</returns>
    IAsyncEnumerable<GatewayConfig> WatchAsync(CancellationToken cancellationToken);
}
