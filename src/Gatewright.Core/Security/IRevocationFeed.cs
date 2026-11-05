// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>Broadcasts key revocations so that every gateway stops accepting a key without waiting for the next snapshot.</summary>
public interface IRevocationFeed
{
    /// <summary>Announces that a key is no longer valid.</summary>
    /// <param name="keyHash">The key hash (see <see cref="ApiKeyHasher"/>).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the revocation is published.</returns>
    ValueTask PublishAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>Streams the revocations announced by any node.</summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>The revoked key hashes.</returns>
    IAsyncEnumerable<string> SubscribeAsync(CancellationToken cancellationToken);
}
