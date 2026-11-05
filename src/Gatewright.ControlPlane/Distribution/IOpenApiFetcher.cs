// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Downloads OpenAPI documents of discovered services.</summary>
public interface IOpenApiFetcher
{
    /// <summary>Downloads a document.</summary>
    /// <param name="uri">The document address.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The document text.</returns>
    Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>Default fetcher using a long-lived HTTP client.</summary>
public sealed class HttpOpenApiFetcher : IOpenApiFetcher, IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(10) };

    /// <inheritdoc/>
    public Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return _client.GetStringAsync(uri, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
