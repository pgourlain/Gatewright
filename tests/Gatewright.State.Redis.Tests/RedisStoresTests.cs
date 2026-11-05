// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;

namespace Gatewright.State.Redis.Tests;

[Collection(RedisTestGroup.Name)]
public sealed class RedisStoresTests(RedisFixture redis)
{
    [Fact]
    public async Task RateLimitStore_GrantsUpToCapacityThenRefillsFromRedisClock()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisRateLimitStore(guard);
        var budget = new RateLimitBudget(10, 100);

        (await store.ReserveAsync("k", budget, 10, default)).Granted.ShouldBe(10);
        var empty = await store.ReserveAsync("k", budget, 50, default);
        empty.Granted.ShouldBeLessThan(50);
        empty.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);

        await Task.Delay(150);

        // 100 tokens per second were refilled by the Redis clock during the pause.
        (await store.ReserveAsync("k", budget, 5, default)).Granted.ShouldBe(5);
    }

    [Fact]
    public async Task RateLimitStore_PartialGrant_ReportsRetryAfter()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisRateLimitStore(guard);
        var budget = new RateLimitBudget(10, 1);
        await store.ReserveAsync("k", budget, 7, default);

        var reservation = await store.ReserveAsync("k", budget, 6, default);

        reservation.Granted.ShouldBeInRange(3, 4);
        reservation.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RateLimitStore_ConcurrentInstances_NeverGrantMoreThanCapacity()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisRateLimitStore(guard);
        var budget = new RateLimitBudget(100, 0.0001);
        var total = 0;

        await Parallel.ForAsync(0, 40, async (_, ct) =>
        {
            var reservation = await store.ReserveAsync("shared", budget, 10, ct);
            Interlocked.Add(ref total, reservation.Granted);
        });

        total.ShouldBe(100);
    }

    [Fact]
    public async Task QuotaStore_IsStrict_UnderConcurrency()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisQuotaStore(guard);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var allowed = 0;

        await Parallel.ForAsync(0, 200, async (_, ct) =>
        {
            if ((await store.TryConsumeAsync("sub-1|2026-01", 50, expires, ct)).Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBe(50);
        var denied = await store.TryConsumeAsync("sub-1|2026-01", 50, expires, default);
        denied.Allowed.ShouldBeFalse();
        denied.Used.ShouldBe(50);
        denied.Remaining.ShouldBe(0);
    }

    [Fact]
    public async Task QuotaStore_CounterExpiresAtTheEndOfThePeriod()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisQuotaStore(guard);

        await store.TryConsumeAsync("short", 5, DateTimeOffset.UtcNow.AddSeconds(2), default);
        await Task.Delay(TimeSpan.FromSeconds(3));
        var again = await store.TryConsumeAsync("short", 5, DateTimeOffset.UtcNow.AddMinutes(1), default);

        again.Used.ShouldBe(1);
    }

    [Fact]
    public async Task QuotaStore_KeysAreIndependent()
    {
        await using var guard = redis.CreateGuard();
        var store = new RedisQuotaStore(guard);
        var expires = DateTimeOffset.UtcNow.AddMinutes(1);

        (await store.TryConsumeAsync("a", 1, expires, default)).Allowed.ShouldBeTrue();
        (await store.TryConsumeAsync("a", 1, expires, default)).Allowed.ShouldBeFalse();
        (await store.TryConsumeAsync("b", 1, expires, default)).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task RevocationFeed_DeliversToAllInstancesQuickly()
    {
        var prefix = $"t{Guid.NewGuid():N}:";
        await using var publisherGuard = redis.CreateGuard(prefix);
        await using var firstGuard = redis.CreateGuard(prefix);
        await using var secondGuard = redis.CreateGuard(prefix);
        var publisher = new RedisRevocationFeed(publisherGuard);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var first = new RedisRevocationFeed(firstGuard).SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await using var second = new RedisRevocationFeed(secondGuard).SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var firstNext = first.MoveNextAsync().AsTask();
        var secondNext = second.MoveNextAsync().AsTask();
        await Task.Delay(500);
        var started = DateTime.UtcNow;

        await publisher.PublishAsync("deadbeef", cts.Token);

        (await firstNext).ShouldBeTrue();
        (await secondNext).ShouldBeTrue();
        first.Current.ShouldBe("deadbeef");
        second.Current.ShouldBe("deadbeef");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RevokedKey_PublishedThroughRedis_IsRejectedByOtherGatewaysWithinFiveSeconds()
    {
        var prefix = $"t{Guid.NewGuid():N}:";
        await using var controlPlaneGuard = redis.CreateGuard(prefix);
        await using var gatewayGuard = redis.CreateGuard(prefix);
        var store = new InMemorySubscriptionStore();
        store.Apply(new Gatewright.Core.Snapshots.SecurityData
        {
            Subscriptions = [new Gatewright.Core.Configuration.SubscriptionDefinition { Id = "s", Product = "p", KeyHashes = ["cafe01"] }],
        });
        using var listener = new Gatewright.DataPlane.RevocationListenerService(
            new RedisRevocationFeed(gatewayGuard),
            [store],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Gatewright.DataPlane.RevocationListenerService>.Instance);
        await listener.StartAsync(default);
        await Task.Delay(500);

        await new RedisRevocationFeed(controlPlaneGuard).PublishAsync("cafe01", default);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await store.FindByKeyHashAsync("cafe01") is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        (await store.FindByKeyHashAsync("cafe01")).ShouldBeNull();
        await listener.StopAsync(default);
    }
}
