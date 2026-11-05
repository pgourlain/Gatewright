// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.State.Redis.Tests;

[Collection(RedisTestGroup.Name)]
public sealed class RedisGuardTests(RedisFixture redis)
{
    private const string Unreachable = "127.0.0.1:1,connectTimeout=200";

    [Fact]
    public async Task UnreachableRedis_FailsFastWithinTheCommandTimeout()
    {
        await using var guard = redis.CreateGuard(connectionString: Unreachable, timeoutMs: 150, threshold: 10);
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<RedisUnavailableException>(async () => await new RedisRateLimitStore(guard).ReserveAsync("k", new(1, 1), 1, default));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConsecutiveFailures_OpenTheCircuitAndLaterCallsAreRejectedInstantly()
    {
        var time = new FakeTimeProvider();
        await using var guard = redis.CreateGuard(connectionString: Unreachable, timeoutMs: 100, threshold: 3, time: time);
        var store = new RedisRateLimitStore(guard);

        for (var i = 0; i < 3; i++)
        {
            await Should.ThrowAsync<RedisUnavailableException>(async () => await store.ReserveAsync("k", new(1, 1), 1, default));
        }

        guard.IsCircuitOpen.ShouldBeTrue();
        var stopwatch = Stopwatch.StartNew();
        var ex = await Should.ThrowAsync<RedisUnavailableException>(async () => await store.ReserveAsync("k", new(1, 1), 1, default));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(50));
        ex.Message.ShouldContain("circuit");
    }

    [Fact]
    public async Task HealthyRedis_KeepsTheCircuitClosed()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisQuotaStore(guard);

        for (var i = 0; i < 20; i++)
        {
            await store.TryConsumeAsync("k", 100, DateTimeOffset.UtcNow.AddMinutes(1), default);
        }

        guard.IsCircuitOpen.ShouldBeFalse();
    }
}
