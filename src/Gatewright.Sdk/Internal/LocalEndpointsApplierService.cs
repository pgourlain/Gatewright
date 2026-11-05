// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Distribution;
using Gatewright.DataPlane;

namespace Gatewright.Sdk.Internal;

/// <summary>In single-process mode, applies discovered endpoints straight to the data plane (no gRPC stream is involved).</summary>
internal sealed class LocalEndpointsApplierService : BackgroundService
{
    private readonly EndpointsHub _hub;
    private readonly SnapshotApplier _applier;

    public LocalEndpointsApplierService(EndpointsHub hub, SnapshotApplier applier)
    {
        _hub = hub;
        _applier = applier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = _hub.Subscribe();
        try
        {
            _applier.ApplyEndpoints(await _hub.GetCurrentAsync(stoppingToken).ConfigureAwait(false));
            await foreach (var endpoints in subscription.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _applier.ApplyEndpoints(endpoints);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
