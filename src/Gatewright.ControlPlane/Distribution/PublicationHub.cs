// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Threading.Channels;
using Gatewright.ControlPlane.Data;
using Gatewright.Distribution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>
/// Watches the database for new publications (every control plane replica does, so a publication made on any replica reaches every connected
/// node), collects changes for a short debounce window and fans the latest one out to subscribers.
/// </summary>
public sealed partial class PublicationHub : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DistributionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PublicationHub> _logger;
    private readonly ConcurrentDictionary<string, EnvironmentState> _environments = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PublicationHub"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="options">The distribution options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PublicationHub(IServiceScopeFactory scopes, IOptions<DistributionOptions> options, TimeProvider time, ILogger<PublicationHub> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Subscribes to the publications of an environment. Updates already pending at subscription time are not replayed: use <see cref="GetCurrentAsync"/> first.</summary>
    /// <param name="environment">The environment name.</param>
    /// <returns>The subscription; dispose it to unsubscribe.</returns>
    public HubSubscription Subscribe(string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        var state = _environments.GetOrAdd(environment, _ => new EnvironmentState());
        var channel = Channel.CreateBounded<PublishedConfig>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (state.Subscribers)
        {
            state.Subscribers.Add(channel);
        }

        return new HubSubscription(channel.Reader, () =>
        {
            lock (state.Subscribers)
            {
                state.Subscribers.Remove(channel);
            }
        });
    }

    /// <summary>Reads the active publication of an environment from the database.</summary>
    /// <param name="environment">The environment name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The publication, or <see langword="null"/> when none was published.</returns>
    public async Task<PublishedConfig?> GetCurrentAsync(string environment, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var found = await db.Environments.AsNoTracking()
            .Where(e => e.Name == environment && e.CurrentPublicationId != null)
            .Join(db.Publications.AsNoTracking(), e => e.CurrentPublicationId, p => p.Id, (_, p) => new { p.Number, p.SnapshotHash, p.SnapshotJson })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return found is null ? null : new PublishedConfig(found.Number, found.SnapshotHash, found.SnapshotJson);
    }

    private async Task<long?> GetCurrentNumberAsync(string environment, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        return await db.Environments.AsNoTracking()
            .Where(e => e.Name == environment && e.CurrentPublicationId != null)
            .Join(db.Publications.AsNoTracking(), e => e.CurrentPublicationId, p => p.Id, (_, p) => (long?)p.Number)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a past publication of an environment, used to compute deltas for nodes that are not at the latest version.</summary>
    /// <param name="environment">The environment name.</param>
    /// <param name="number">The publication number.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The publication, or <see langword="null"/>.</returns>
    public async Task<PublishedConfig?> GetAsync(string environment, long number, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var found = await db.Publications.AsNoTracking()
            .Where(p => p.Number == number && db.Environments.Any(e => e.Id == p.EnvironmentId && e.Name == environment))
            .Select(p => new { p.Number, p.SnapshotHash, p.SnapshotJson })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return found is null ? null : new PublishedConfig(found.Number, found.SnapshotHash, found.SnapshotJson);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(Math.Max(20, _options.PollMilliseconds));
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _options.DebounceMilliseconds));
        var quick = TimeSpan.FromMilliseconds(Math.Max(20, Math.Min(poll.TotalMilliseconds, debounce.TotalMilliseconds / 3)));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var anyPending = false;
                foreach (var (name, state) in _environments)
                {
                    try
                    {
                        anyPending |= await CheckAsync(name, state, debounce, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogCheckFailed(_logger, ex, name);
                    }
                }

                await Task.Delay(anyPending ? quick : poll, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<bool> CheckAsync(string environment, EnvironmentState state, TimeSpan debounce, CancellationToken cancellationToken)
    {
        int subscribers;
        lock (state.Subscribers)
        {
            subscribers = state.Subscribers.Count;
        }

        if (subscribers == 0)
        {
            state.Reset();
            return false;
        }

        // Idle polls only read the number of the active publication (a few bytes): the snapshot JSON is loaded when it is broadcast.
        var number = await GetCurrentNumberAsync(environment, cancellationToken).ConfigureAwait(false);
        if (number is null)
        {
            return false;
        }

        if (number == state.LastBroadcast)
        {
            state.PendingNumber = null;
            return false;
        }

        var now = _time.GetUtcNow();
        if (state.PendingNumber != number)
        {
            state.PendingNumber = number;
            state.PendingSince = now;
        }

        // The first publication seen for an environment is broadcast at once (subscribers ignore versions they already have).
        if (state.LastBroadcast is not null && now - state.PendingSince < debounce)
        {
            return true;
        }

        var current = await GetCurrentAsync(environment, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return false;
        }

        state.LastBroadcast = current.Number;
        state.PendingNumber = null;
        Channel<PublishedConfig>[] targets;
        lock (state.Subscribers)
        {
            targets = [.. state.Subscribers];
        }

        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(current);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check publications of environment {Environment}.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception, string environment);

    private sealed class EnvironmentState
    {
        public List<Channel<PublishedConfig>> Subscribers { get; } = [];

        public long? LastBroadcast { get; set; }

        public long? PendingNumber { get; set; }

        public DateTimeOffset PendingSince { get; set; }

        public void Reset()
        {
            LastBroadcast = null;
            PendingNumber = null;
        }
    }
}

/// <summary>A subscription to the publications of one environment.</summary>
public sealed class HubSubscription : IDisposable
{
    private readonly Action _unsubscribe;

    internal HubSubscription(ChannelReader<PublishedConfig> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    /// <summary>Gets the reader of publications; only the latest unread one is kept.</summary>
    public ChannelReader<PublishedConfig> Reader { get; }

    /// <inheritdoc/>
    public void Dispose() => _unsubscribe();
}
