// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatewright.DataPlane;

/// <summary>Applies key revocations announced by the control plane (or any node) right away, without waiting for the next snapshot.</summary>
public sealed partial class RevocationListenerService : BackgroundService
{
    private readonly IRevocationFeed _feed;
    private readonly IReadOnlyList<IRevocationSink> _sinks;
    private readonly ILogger<RevocationListenerService> _logger;

    /// <summary>Initializes a new instance of the <see cref="RevocationListenerService"/> class.</summary>
    /// <param name="feed">The revocation feed.</param>
    /// <param name="sinks">The receivers of revocations.</param>
    /// <param name="logger">The logger.</param>
    public RevocationListenerService(IRevocationFeed feed, IEnumerable<IRevocationSink> sinks, ILogger<RevocationListenerService> logger)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(logger);
        _feed = feed;
        _sinks = [.. sinks];
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var keyHash in _feed.SubscribeAsync(stoppingToken).ConfigureAwait(false))
                {
                    foreach (var sink in _sinks)
                    {
                        sink.Revoke(keyHash);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogFeedFailed(_logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The revocation feed failed; retrying. Revocations are still applied with the next snapshot.")]
    private static partial void LogFeedFailed(ILogger logger, Exception exception);
}
