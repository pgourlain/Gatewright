// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Gatewright.Policies.Jwt;

/// <summary>Registry of JWKS handles plus the logic that refreshes them. Network access happens only in <see cref="RefreshDueAsync"/>.</summary>
public sealed partial class JwksStore
{
    private readonly ConcurrentDictionary<string, JwksHandle> _handles = new(StringComparer.Ordinal);
    private readonly IJwksFetcher _fetcher;
    private readonly TimeProvider _time;
    private readonly ILogger<JwksStore> _logger;

    /// <summary>Initializes a new instance of the <see cref="JwksStore"/> class.</summary>
    /// <param name="fetcher">The document downloader.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public JwksStore(IJwksFetcher fetcher, TimeProvider time, ILogger<JwksStore> logger)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _fetcher = fetcher;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets or sets the shortest delay between two refreshes triggered by an unknown key id.</summary>
    public TimeSpan MinimumRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the registered handles.</summary>
    public IReadOnlyCollection<JwksHandle> Handles => (IReadOnlyCollection<JwksHandle>)_handles.Values;

    /// <summary>Registers (or returns the existing) handle for a key source. Does not download anything.</summary>
    /// <param name="jwksUri">The JWKS address, or <see langword="null"/> when <paramref name="metadataAddress"/> is used.</param>
    /// <param name="metadataAddress">The OpenID Connect metadata address, or <see langword="null"/>.</param>
    /// <param name="refreshInterval">The background refresh interval.</param>
    /// <returns>The handle.</returns>
    public JwksHandle Register(Uri? jwksUri, Uri? metadataAddress, TimeSpan refreshInterval)
    {
        if (jwksUri is null == metadataAddress is null)
        {
            throw new ArgumentException("Provide exactly one of jwksUri or metadataAddress.");
        }

        var key = (jwksUri ?? metadataAddress)!.AbsoluteUri;
        return _handles.GetOrAdd(key, _ => new JwksHandle(jwksUri, metadataAddress, refreshInterval));
    }

    /// <summary>Refreshes every handle that is due. Failures keep the previous keys.</summary>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>A task completing when all due handles were processed.</returns>
    public Task RefreshDueAsync(CancellationToken cancellationToken) => RefreshAsync(force: false, cancellationToken);

    /// <summary>Refreshes every handle now, whether due or not.</summary>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>A task completing when all handles were processed.</returns>
    public Task RefreshAllAsync(CancellationToken cancellationToken) => RefreshAsync(force: true, cancellationToken);

    private async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        foreach (var handle in _handles.Values)
        {
            if (!force && !handle.IsRefreshDue(now, MinimumRefreshInterval))
            {
                continue;
            }

            try
            {
                var jwksUri = handle.JwksUri ?? await DiscoverAsync(handle.MetadataAddress!, cancellationToken).ConfigureAwait(false);
                var json = await _fetcher.FetchAsync(jwksUri, cancellationToken).ConfigureAwait(false);
                var keys = new JsonWebKeySet(json).GetSigningKeys();
                handle.SetKeys([.. keys], now);
                LogRefreshed(_logger, jwksUri.AbsoluteUri, keys.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException or InvalidOperationException or TaskCanceledException)
            {
                LogRefreshFailed(_logger, (handle.JwksUri ?? handle.MetadataAddress)!.AbsoluteUri, ex.Message);
            }
        }
    }

    private async Task<Uri> DiscoverAsync(Uri metadataAddress, CancellationToken cancellationToken)
    {
        var json = await _fetcher.FetchAsync(metadataAddress, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("jwks_uri").GetString();
        return new Uri(value!, UriKind.Absolute);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refreshed {Count} signing keys from {Uri}.")]
    private static partial void LogRefreshed(ILogger logger, string uri, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not refresh signing keys from {Uri}, keeping the previous ones: {Reason}")]
    private static partial void LogRefreshFailed(ILogger logger, string uri, string reason);
}
