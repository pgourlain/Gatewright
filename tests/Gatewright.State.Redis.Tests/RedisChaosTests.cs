// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Sockets;
using Gatewright.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.Redis;

namespace Gatewright.State.Redis.Tests;

/// <summary>Redis killed while the gateway is using it, then brought back (D8: controlled degradation, then recovery). Uses its own container so the shared fixture is never disturbed.</summary>
public sealed class RedisChaosTests
{
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task RedisKilledMidTraffic_DegradesThenRecoversOnceItIsBack()
    {
        var port = FreePort();
        await using var container = new RedisBuilder("redis:7-alpine").WithPortBinding(port, 6379).Build();
        await container.StartAsync();
        var time = new FakeTimeProvider();
        await using var guard = new RedisGuard(
            Options.Create(new RedisOptions
            {
                ConnectionString = $"127.0.0.1:{port},connectTimeout=300,abortConnect=false",
                KeyPrefix = $"chaos{Guid.NewGuid():N}:",
                CommandTimeoutMilliseconds = 200,
                FailureThreshold = 3,
                BreakSeconds = 30,
            }),
            time,
            NullLogger<RedisGuard>.Instance);
        var store = new RedisRateLimitStore(guard);

        _ = await store.ReserveAsync("k", new(1000, 60), 1, default);

        await container.StopAsync();
        for (var i = 0; i < 3; i++)
        {
            await Should.ThrowAsync<RedisUnavailableException>(async () => await store.ReserveAsync("k", new(1000, 60), 1, default));
        }

        guard.IsCircuitOpen.ShouldBeTrue();

        await container.StartAsync();
        time.Advance(TimeSpan.FromSeconds(31));

        RateLimitReservation? recovered = null;
        for (var attempt = 0; attempt < 20 && recovered is null; attempt++)
        {
            try
            {
                recovered = await store.ReserveAsync("k", new(1000, 60), 1, default);
            }
            catch (RedisUnavailableException)
            {
                await Task.Delay(250);
                time.Advance(TimeSpan.FromSeconds(31));
            }
        }

        recovered.HasValue.ShouldBeTrue();
        guard.IsCircuitOpen.ShouldBeFalse();
    }
}
