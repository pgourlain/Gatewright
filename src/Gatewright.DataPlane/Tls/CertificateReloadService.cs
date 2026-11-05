// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Tls;

/// <summary>Periodically reloads certificate files that changed (for example a Kubernetes TLS secret rotated by cert-manager), without restart or dropped connections.</summary>
public sealed class CertificateReloadService : BackgroundService
{
    private readonly CertificateStore _store;
    private readonly TimeSpan _interval;

    /// <summary>Initializes a new instance of the <see cref="CertificateReloadService"/> class.</summary>
    /// <param name="store">The certificate store.</param>
    /// <param name="options">The TLS options.</param>
    public CertificateReloadService(CertificateStore store, IOptions<TlsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ReloadSeconds));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                _store.ReloadChanged();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
