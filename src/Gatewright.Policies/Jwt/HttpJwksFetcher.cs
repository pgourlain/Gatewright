// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Jwt;

/// <summary>Default <see cref="IJwksFetcher"/> using a single long-lived <see cref="HttpClient"/> with a bounded connection lifetime.</summary>
public sealed class HttpJwksFetcher : IJwksFetcher, IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <inheritdoc/>
    public Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return _client.GetStringAsync(uri, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
