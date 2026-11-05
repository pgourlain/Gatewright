// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.Kubernetes;

/// <summary>
/// Runs on every control plane replica but works only on the leader: watches Services and EndpointSlices, and writes the resulting
/// <see cref="DiscoveryState"/> to the store (debounced) for every replica to serve.
/// </summary>
public sealed partial class KubernetesDiscoveryService : BackgroundService
{
    private readonly IKubernetes _client;
    private readonly ILeaderElection _leader;
    private readonly IDiscoveryStore _store;
    private readonly KubernetesOptions _options;
    private readonly ILogger<KubernetesDiscoveryService> _logger;

    /// <summary>Initializes a new instance of the <see cref="KubernetesDiscoveryService"/> class.</summary>
    /// <param name="client">The Kubernetes client.</param>
    /// <param name="leader">The leader election.</param>
    /// <param name="store">Where discovered state is written.</param>
    /// <param name="options">The Kubernetes options.</param>
    /// <param name="logger">The logger.</param>
    public KubernetesDiscoveryService(
        IKubernetes client,
        ILeaderElection leader,
        IDiscoveryStore store,
        IOptions<KubernetesOptions> options,
        ILogger<KubernetesDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _leader = leader;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var term = await _leader.WaitForLeadershipAsync(stoppingToken).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, term);
                await RunAsLeaderAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Leadership was lost: go back to waiting.
            }
            catch (Exception ex)
            {
                LogFailed(_logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsLeaderAsync(CancellationToken cancellationToken)
    {
        var aggregator = new ClusterStateAggregator(_options.AnnotationPrefix);
        using var changes = new SemaphoreSlim(0, 1);
        void Changed()
        {
            try
            {
                changes.Release();
            }
            catch (SemaphoreFullException)
            {
                // A write is already pending.
            }
        }

        var services = new ResourceWatcher<V1Service, V1ServiceList>(
            "services",
            ct => _client.CoreV1.ListServiceForAllNamespacesAsync(cancellationToken: ct),
            (rv, ct) => _client.CoreV1.WatchListServiceForAllNamespacesAsync(resourceVersion: rv, allowWatchBookmarks: true, timeoutSeconds: 300, cancellationToken: ct),
            aggregator.ClearServices,
            aggregator.UpsertService,
            aggregator.RemoveService,
            Changed,
            _logger);
        var slices = new ResourceWatcher<V1EndpointSlice, V1EndpointSliceList>(
            "endpointslices",
            ct => _client.DiscoveryV1.ListEndpointSliceForAllNamespacesAsync(cancellationToken: ct),
            (rv, ct) => _client.DiscoveryV1.WatchListEndpointSliceForAllNamespacesAsync(resourceVersion: rv, allowWatchBookmarks: true, timeoutSeconds: 300, cancellationToken: ct),
            aggregator.ClearSlices,
            aggregator.UpsertSlice,
            aggregator.RemoveSlice,
            Changed,
            _logger);

        var tasks = new[] { services.RunAsync(cancellationToken), slices.RunAsync(cancellationToken), WriteLoopAsync(aggregator, changes, cancellationToken) };
        await Task.WhenAny(tasks).ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task WriteLoopAsync(ClusterStateAggregator aggregator, SemaphoreSlim changes, CancellationToken cancellationToken)
    {
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _options.DebounceMilliseconds));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await changes.WaitAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(debounce, cancellationToken).ConfigureAwait(false);
                try
                {
                    await _store.SaveAsync(aggregator.Snapshot(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSaveFailed(_logger, ex);
                    Changed(changes);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping or leadership lost.
        }
    }

    private static void Changed(SemaphoreSlim changes)
    {
        try
        {
            changes.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already pending.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Kubernetes discovery failed; retrying.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the discovered state; retrying.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
