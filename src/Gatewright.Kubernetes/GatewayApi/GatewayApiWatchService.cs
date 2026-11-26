// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Discovery;
using k8s;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>
/// Runs on every control plane replica but works only on the leader: watches <c>Gateway</c> and <c>HTTPRoute</c> objects, maps the routes
/// that Gatewright serves and hands them to the <see cref="IGatewayApiSink"/>, on every change (debounced) and periodically (ADR 0017).
/// </summary>
public sealed partial class GatewayApiWatchService : BackgroundService
{
    private readonly IKubernetes _client;
    private readonly ILeaderElection _leader;
    private readonly IGatewayApiSink _sink;
    private readonly GatewayApiReferences _references;
    private readonly KubernetesOptions _kubernetes;
    private readonly GatewayApiOptions _options;
    private readonly RouteStatusWriter? _status;
    private readonly ILogger<GatewayApiWatchService> _logger;

    /// <summary>Initializes a new instance of the <see cref="GatewayApiWatchService"/> class.</summary>
    /// <param name="client">The Kubernetes client.</param>
    /// <param name="leader">The leader election.</param>
    /// <param name="sink">Where the routes are applied.</param>
    /// <param name="references">Receives the Services referenced by the served routes.</param>
    /// <param name="kubernetes">The Kubernetes options.</param>
    /// <param name="options">The Gateway API options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="status">Writes the outcome to the route status, when <c>GatewayApi:WriteStatus</c> is set.</param>
    public GatewayApiWatchService(
        IKubernetes client,
        ILeaderElection leader,
        IGatewayApiSink sink,
        GatewayApiReferences references,
        IOptions<KubernetesOptions> kubernetes,
        IOptions<GatewayApiOptions> options,
        ILogger<GatewayApiWatchService> logger,
        RouteStatusWriter? status = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(kubernetes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _leader = leader;
        _sink = sink;
        _references = references;
        _kubernetes = kubernetes.Value;
        _options = options.Value;
        _status = options.Value.WriteStatus ? status : null;
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
        var aggregator = new GatewayApiAggregator(_kubernetes.AnnotationPrefix, _options.GatewayClassName);
        using var changes = new SemaphoreSlim(0, 1);
        var routesListed = false;
        var gatewaysListed = false;

        void Signal()
        {
            try
            {
                changes.Release();
            }
            catch (SemaphoreFullException)
            {
                // A reconciliation is already pending.
            }
        }

        var gateways = new ResourceWatcher<Gateway, GatewayList>(
            "gateways",
            async ct => Read<GatewayList>(await _client.CustomObjects.ListClusterCustomObjectAsync(GatewayApiResources.Group, GatewayApiResources.Version, GatewayApiResources.Gateways, cancellationToken: ct).ConfigureAwait(false)),
            (rv, ct) => Watch<Gateway>(GatewayApiResources.Gateways, rv, ct),
            aggregator.ClearGateways,
            aggregator.UpsertGateway,
            aggregator.RemoveGateway,
            () =>
            {
                gatewaysListed = true;
                Signal();
            },
            _logger);
        var routes = new ResourceWatcher<HttpRoute, HttpRouteList>(
            "httproutes",
            async ct => Read<HttpRouteList>(await _client.CustomObjects.ListClusterCustomObjectAsync(GatewayApiResources.Group, GatewayApiResources.Version, GatewayApiResources.HttpRoutes, cancellationToken: ct).ConfigureAwait(false)),
            (rv, ct) => Watch<HttpRoute>(GatewayApiResources.HttpRoutes, rv, ct),
            aggregator.ClearRoutes,
            aggregator.UpsertRoute,
            aggregator.RemoveRoute,
            () =>
            {
                routesListed = true;
                Signal();
            },
            _logger);

        var loop = ReconcileLoopAsync(aggregator, changes, () => routesListed && gatewaysListed, cancellationToken);
        var tasks = new[] { gateways.RunAsync(cancellationToken), routes.RunAsync(cancellationToken), loop };
        await Task.WhenAny(tasks).ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ReconcileLoopAsync(GatewayApiAggregator aggregator, SemaphoreSlim changes, Func<bool> listed, CancellationToken cancellationToken)
    {
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _options.DebounceMilliseconds));
        var period = TimeSpan.FromSeconds(Math.Max(1, _options.ReconcileSeconds));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wakes on a change, or after the period so that absences are counted and a missed event is repaired.
                await changes.WaitAsync(period, cancellationToken).ConfigureAwait(false);
                await Task.Delay(debounce, cancellationToken).ConfigureAwait(false);
                if (!listed())
                {
                    continue;
                }

                try
                {
                    var routes = aggregator.Snapshot();
                    _references.Set(GatewayApiAggregator.ReferencedServices(routes));
                    var results = await _sink.ApplyAsync(new GatewayApiState(routes), cancellationToken).ConfigureAwait(false);
                    if (_status is not null)
                    {
                        await _status.WriteAsync(results, aggregator.ServedGateways(), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogReconcileFailed(_logger, ex);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping or leadership lost.
        }
    }

    private static T Read<T>(object response) =>
        KubernetesJson.Deserialize<T>(response is JsonElement element ? element.GetRawText() : KubernetesJson.Serialize(response));

    private async IAsyncEnumerable<(WatchEventType Type, T Item)> Watch<T>(string plural, string? resourceVersion, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (type, item) in _client.CustomObjects.WatchListClusterCustomObjectAsync(
            GatewayApiResources.Group,
            GatewayApiResources.Version,
            plural,
            allowWatchBookmarks: true,
            resourceVersion: resourceVersion,
            timeoutSeconds: 300,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return (type, Read<T>(item));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Gateway API source failed; retrying.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gateway API reconciliation failed; retrying.")]
    private static partial void LogReconcileFailed(ILogger logger, Exception exception);
}
