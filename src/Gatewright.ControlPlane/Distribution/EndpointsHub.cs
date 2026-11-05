// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Threading.Channels;
using Gatewright.Core.Discovery;
using Gatewright.Distribution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Watches the stored discovery state and pushes endpoint changes to every connected node, whatever its environment.</summary>
public sealed partial class EndpointsHub : BackgroundService
{
    private readonly DbDiscoveryStore _store;
    private readonly DistributionOptions _options;
    private readonly ILogger<EndpointsHub> _logger;
    private readonly List<Channel<ServiceEndpointSet>> _subscribers = [];
    private string _lastHash = "";

    /// <summary>Initializes a new instance of the <see cref="EndpointsHub"/> class.</summary>
    /// <param name="store">The discovery store.</param>
    /// <param name="options">The distribution options.</param>
    /// <param name="logger">The logger.</param>
    public EndpointsHub(DbDiscoveryStore store, IOptions<DistributionOptions> options, ILogger<EndpointsHub> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Reads the current endpoints.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The endpoints.</returns>
    public async Task<ServiceEndpointSet> GetCurrentAsync(CancellationToken cancellationToken) =>
        (await _store.LoadAsync(cancellationToken).ConfigureAwait(false)).Endpoints;

    /// <summary>Subscribes to endpoint changes; only the latest unread set is kept.</summary>
    /// <returns>The subscription.</returns>
    public EndpointsSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<ServiceEndpointSet>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_subscribers)
        {
            _subscribers.Add(channel);
        }

        return new EndpointsSubscription(channel.Reader, () =>
        {
            lock (_subscribers)
            {
                _subscribers.Remove(channel);
            }
        });
    }

    private const double NoSubscriberPollMilliseconds = 1000;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(Math.Max(20, _options.PollMilliseconds));

        // Nothing needs the result when no data plane is connected: do not query the database at all, just look again later.
        var idlePoll = TimeSpan.FromMilliseconds(Math.Max(poll.TotalMilliseconds, NoSubscriberPollMilliseconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool hasSubscribers;
                lock (_subscribers)
                {
                    hasSubscribers = _subscribers.Count > 0;
                }

                if (!hasSubscribers)
                {
                    await Task.Delay(idlePoll, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var hash = await _store.LoadEndpointsHashAsync(stoppingToken).ConfigureAwait(false);
                    if (hash != _lastHash)
                    {
                        var endpoints = await GetCurrentAsync(stoppingToken).ConfigureAwait(false);
                        _lastHash = endpoints.Hash() == hash ? hash : endpoints.Hash();
                        Channel<ServiceEndpointSet>[] targets;
                        lock (_subscribers)
                        {
                            targets = [.. _subscribers];
                        }

                        foreach (var channel in targets)
                        {
                            channel.Writer.TryWrite(endpoints);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(_logger, ex);
                }

                await Task.Delay(poll, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the discovered endpoints.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>A subscription to endpoint changes.</summary>
public sealed class EndpointsSubscription : IDisposable
{
    private readonly Action _unsubscribe;

    internal EndpointsSubscription(ChannelReader<ServiceEndpointSet> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    /// <summary>Gets the reader of endpoint sets.</summary>
    public ChannelReader<ServiceEndpointSet> Reader { get; }

    /// <inheritdoc/>
    public void Dispose() => _unsubscribe();
}
