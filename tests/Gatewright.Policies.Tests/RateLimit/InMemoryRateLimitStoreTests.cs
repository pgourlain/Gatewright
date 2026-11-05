// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.Policies.Tests.RateLimit;

public sealed class InMemoryRateLimitStoreTests
{
    [Fact]
    public async Task Reserve_GrantsUpToCapacityThenRefills()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore(time);
        var budget = new RateLimitBudget(10, 1);

        (await store.ReserveAsync("k", budget, 6, default)).Granted.ShouldBe(6);
        var second = await store.ReserveAsync("k", budget, 6, default);
        second.Granted.ShouldBe(4);
        second.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);

        time.Advance(TimeSpan.FromSeconds(3));

        (await store.ReserveAsync("k", budget, 5, default)).Granted.ShouldBe(3);
    }

    [Fact]
    public async Task Revocation_Feed_DeliversToEverySubscriber()
    {
        var feed = new InMemoryRevocationFeed();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = feed.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await using var second = feed.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var firstNext = first.MoveNextAsync().AsTask();
        var secondNext = second.MoveNextAsync().AsTask();
        await Task.Delay(50);

        await feed.PublishAsync("abc", cts.Token);

        (await firstNext).ShouldBeTrue();
        (await secondNext).ShouldBeTrue();
        first.Current.ShouldBe("abc");
        second.Current.ShouldBe("abc");
    }

    [Fact]
    public async Task SubscriptionStore_RevokedKey_IsRejectedUntilNextSnapshot()
    {
        var store = new InMemorySubscriptionStore();
        var data = new Gatewright.Core.Snapshots.SecurityData
        {
            Subscriptions = [new Gatewright.Core.Configuration.SubscriptionDefinition { Id = "s", Product = "p", KeyHashes = ["AbC123"] }],
        };
        store.Apply(data);
        (await store.FindByKeyHashAsync("abc123")).ShouldNotBeNull();

        store.Revoke("ABC123");

        (await store.FindByKeyHashAsync("abc123")).ShouldBeNull();
        store.Apply(new Gatewright.Core.Snapshots.SecurityData());
        (await store.FindByKeyHashAsync("abc123")).ShouldBeNull();
    }
}
