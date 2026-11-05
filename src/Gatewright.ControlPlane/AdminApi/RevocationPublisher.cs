// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Microsoft.Extensions.Logging;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Announces revoked keys to the gateways. A failure never fails the Admin API call: the revocation also reaches gateways with the next publication.</summary>
public sealed partial class RevocationPublisher
{
    private readonly IRevocationFeed _feed;
    private readonly ILogger<RevocationPublisher> _logger;

    /// <summary>Initializes a new instance of the <see cref="RevocationPublisher"/> class.</summary>
    /// <param name="feed">The revocation feed.</param>
    /// <param name="logger">The logger.</param>
    public RevocationPublisher(IRevocationFeed feed, ILogger<RevocationPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(logger);
        _feed = feed;
        _logger = logger;
    }

    /// <summary>Announces the revocation of keys.</summary>
    /// <param name="keyHashes">The hashes of the revoked keys.</param>
    /// <param name="cancellationToken">Cancels the announcement.</param>
    /// <returns>A task completing when all keys were announced (or failed to be).</returns>
    public async Task PublishAsync(IEnumerable<string> keyHashes, CancellationToken cancellationToken)
    {
        foreach (var hash in keyHashes)
        {
            try
            {
                await _feed.PublishAsync(hash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not announce a key revocation to the gateways; it will apply with the next publication.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
