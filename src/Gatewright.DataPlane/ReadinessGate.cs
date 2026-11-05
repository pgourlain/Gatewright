// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane;

/// <summary>Shutdown behavior settings, bound from <c>Gateway:Shutdown</c>.</summary>
public sealed class ShutdownOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Gateway:Shutdown";

    /// <summary>Gets or sets how long the node keeps serving after readiness turned to failing, so that load balancers stop sending it traffic before connections are drained.</summary>
    public int DrainDelaySeconds { get; set; }
}

/// <summary>Tells whether the node is draining. Readiness fails as soon as shutdown starts; Kestrel then drains in-flight requests.</summary>
public sealed class ReadinessGate : IHostedService
{
    private readonly TimeSpan _delay;
    private int _draining;

    /// <summary>Initializes a new instance of the <see cref="ReadinessGate"/> class.</summary>
    /// <param name="options">The shutdown options.</param>
    public ReadinessGate(IOptions<ShutdownOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _delay = TimeSpan.FromSeconds(Math.Max(0, options.Value.DrainDelaySeconds));
    }

    /// <summary>Gets a value indicating whether shutdown has started.</summary>
    public bool IsDraining => Volatile.Read(ref _draining) == 1;

    /// <summary>Marks the node as draining (also done automatically when the host stops).</summary>
    public void BeginDrain() => Interlocked.Exchange(ref _draining, 1);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        BeginDrain();
        if (_delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host ran out of shutdown time: stop now.
            }
        }
    }
}
