// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Distribution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatewright.DataPlane;

/// <summary>Applies every snapshot received from the distributor to the data plane.</summary>
public sealed partial class SnapshotSubscriptionService : BackgroundService
{
    private readonly IConfigDistributor _distributor;
    private readonly SnapshotApplier _applier;
    private readonly ILogger<SnapshotSubscriptionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="SnapshotSubscriptionService"/> class.</summary>
    /// <param name="distributor">The distributor.</param>
    /// <param name="applier">The snapshot applier.</param>
    /// <param name="logger">The logger.</param>
    public SnapshotSubscriptionService(IConfigDistributor distributor, SnapshotApplier applier, ILogger<SnapshotSubscriptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(distributor);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(logger);
        _distributor = distributor;
        _applier = applier;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var snapshot in _distributor.SubscribeAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _applier.ApplyAsync(snapshot).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogApplyFailed(_logger, ex, snapshot.Version);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected failure applying configuration version {Version}.")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception, long version);
}
