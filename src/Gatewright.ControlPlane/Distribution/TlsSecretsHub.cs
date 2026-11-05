// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Threading.Channels;
using Gatewright.ControlPlane.Data;
using Gatewright.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>
/// Watches the declared hostnames for Kubernetes-secret certificate sources and keeps their material in memory (never persisted),
/// pushing changes to every connected node. Runs independently on every control plane replica: each fetches its own copy, so no
/// cross-replica coordination is needed. Does nothing without an <see cref="ISecretFetcher"/> (Kubernetes discovery disabled).
/// </summary>
public sealed partial class TlsSecretsHub : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ISecretFetcher? _fetcher;
    private readonly TlsSecretsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TlsSecretsHub> _logger;
    private readonly List<Channel<CertificateMaterialSet>> _subscribers = [];
    private CertificateMaterialSet _current = CertificateMaterialSet.Empty;
    private string _lastHash = "";

    /// <summary>Initializes a new instance of the <see cref="TlsSecretsHub"/> class.</summary>
    /// <param name="scopes">The scope factory, to read the declared hostnames.</param>
    /// <param name="options">The options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="fetcher">Reads a secret's material; without one (no Kubernetes integration) this hub stays empty.</param>
    public TlsSecretsHub(
        IServiceScopeFactory scopes,
        IOptions<TlsSecretsOptions> options,
        TimeProvider time,
        ILogger<TlsSecretsHub> logger,
        ISecretFetcher? fetcher = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _fetcher = fetcher;
    }

    /// <summary>Gets the current certificate material.</summary>
    public CertificateMaterialSet GetCurrent() => Volatile.Read(ref _current);

    /// <summary>Subscribes to changes; only the latest unread set is kept.</summary>
    /// <returns>The subscription.</returns>
    public TlsSecretsSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<CertificateMaterialSet>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_subscribers)
        {
            _subscribers.Add(channel);
        }

        return new TlsSecretsSubscription(channel.Reader, () =>
        {
            lock (_subscribers)
            {
                _subscribers.Remove(channel);
            }
        });
    }

    /// <summary>Checks the declared hostnames once and refreshes any changed secret.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_fetcher is null)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var hostnames = await db.Hostnames.AsNoTracking().Select(h => h.Certificate).ToListAsync(cancellationToken).ConfigureAwait(false);
        var references = hostnames.Where(c => c?.Secret is not null).Select(c => c!.Secret!).Distinct(StringComparer.Ordinal).ToList();

        var previous = Volatile.Read(ref _current).Secrets;
        var materials = new Dictionary<string, CertificateMaterial>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var material = await _fetcher!.FetchAsync(reference, cancellationToken).ConfigureAwait(false);
            if (material is not null)
            {
                materials[reference] = material;
            }
            else if (previous.TryGetValue(reference, out var kept))
            {
                // A fetch that fails temporarily never drops a certificate that was already loaded.
                materials[reference] = kept;
            }
        }

        var set = new CertificateMaterialSet(materials);
        var hash = set.Hash();
        if (hash == _lastHash)
        {
            return;
        }

        _lastHash = hash;
        Volatile.Write(ref _current, set);
        Channel<CertificateMaterialSet>[] targets;
        lock (_subscribers)
        {
            targets = [.. _subscribers];
        }

        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(set);
        }

        LogRefreshed(_logger, set.Secrets.Count);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_fetcher is null)
        {
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogRunFailed(_logger, ex);
                }

                await Task.Delay(poll, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshed TLS secret material ({Count} secrets).")]
    private static partial void LogRefreshed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not refresh TLS secret material; will retry.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}

/// <summary>A subscription to TLS secret material changes.</summary>
public sealed class TlsSecretsSubscription : IDisposable
{
    private readonly Action _unsubscribe;

    internal TlsSecretsSubscription(ChannelReader<CertificateMaterialSet> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    /// <summary>Gets the reader of certificate material sets.</summary>
    public ChannelReader<CertificateMaterialSet> Reader { get; }

    /// <inheritdoc/>
    public void Dispose() => _unsubscribe();
}
