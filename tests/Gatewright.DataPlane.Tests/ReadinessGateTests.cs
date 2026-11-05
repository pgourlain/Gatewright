// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Tests;

public sealed class ReadinessGateTests
{
    [Fact]
    public async Task Stop_MarksTheNodeDrainingThenWaitsForTheDrainDelay()
    {
        var gate = new ReadinessGate(Options.Create(new ShutdownOptions { DrainDelaySeconds = 1 }));
        gate.IsDraining.ShouldBeFalse();
        var stopwatch = Stopwatch.StartNew();

        var stopping = gate.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        gate.IsDraining.ShouldBeTrue();
        await stopping;

        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task Stop_WithoutDelay_ReturnsImmediately()
    {
        var gate = new ReadinessGate(Options.Create(new ShutdownOptions()));

        await gate.StopAsync(CancellationToken.None);

        gate.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_WhenTheHostRunsOutOfTime_StopsEarly()
    {
        var gate = new ReadinessGate(Options.Create(new ShutdownOptions { DrainDelaySeconds = 30 }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await gate.StopAsync(cts.Token);

        gate.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_AfterBeginDrainAlreadyRan_SkipsTheDelay()
    {
        // Mirrors the Kubernetes preStop hook (GET /gw/lifecycle/prestop): it calls BeginDrain and
        // waits out the delay itself, before SIGTERM triggers StopAsync. StopAsync must not wait again.
        var gate = new ReadinessGate(Options.Create(new ShutdownOptions { DrainDelaySeconds = 30 }));
        gate.BeginDrain();
        var stopwatch = Stopwatch.StartNew();

        await gate.StopAsync(CancellationToken.None);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
