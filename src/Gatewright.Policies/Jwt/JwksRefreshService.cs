// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Hosting;

namespace Gatewright.Policies.Jwt;

/// <summary>Background service that keeps JWKS fresh so that the request path never touches the network.</summary>
public sealed class JwksRefreshService : BackgroundService
{
    private readonly JwksStore _store;
    private readonly TimeSpan _tick;

    /// <summary>Initializes a new instance of the <see cref="JwksRefreshService"/> class.</summary>
    /// <param name="store">The JWKS store.</param>
    /// <param name="tick">How often the store is checked for due refreshes.</param>
    public JwksRefreshService(JwksStore store, TimeSpan? tick = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _tick = tick ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_tick);
        try
        {
            do
            {
                await _store.RefreshDueAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
