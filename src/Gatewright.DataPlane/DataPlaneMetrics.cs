// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;

namespace Gatewright.DataPlane;

/// <summary>Metrics of the data plane: the configuration version currently applied, to spot nodes that diverge.</summary>
public sealed class DataPlaneMetrics : IHostedService, IDisposable
{
    /// <summary>The meter name.</summary>
    public const string MeterName = "Gatewright.DataPlane";

    private readonly Meter _meter = new(MeterName);

    /// <summary>Initializes a new instance of the <see cref="DataPlaneMetrics"/> class.</summary>
    /// <param name="registry">The pipeline registry holding the applied snapshot.</param>
    public DataPlaneMetrics(PipelineRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _meter.CreateObservableGauge("gatewright.config.version_applied", () => registry.Current?.Version ?? 0, description: "Configuration version applied by this node.");
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
