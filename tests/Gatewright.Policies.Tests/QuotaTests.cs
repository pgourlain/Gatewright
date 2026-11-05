// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.Policies.Tests;

public sealed class QuotaTests
{
    [Fact]
    public async Task InMemoryStore_ConsumesUntilLimit()
    {
        var store = new InMemoryQuotaStore();
        var expires = DateTimeOffset.UtcNow.AddDays(1);

        var results = new List<QuotaResult>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await store.TryConsumeAsync("k", 3, expires, CancellationToken.None));
        }

        results.Select(r => r.Allowed).ShouldBe([true, true, true, false]);
        results[2].Remaining.ShouldBe(0);
        results[0].Remaining.ShouldBe(2);
    }

    [Fact]
    public async Task InMemoryStore_KeysAreIndependent()
    {
        var store = new InMemoryQuotaStore();
        var expires = DateTimeOffset.UtcNow.AddDays(1);

        (await store.TryConsumeAsync("a", 1, expires, CancellationToken.None)).Allowed.ShouldBeTrue();
        (await store.TryConsumeAsync("a", 1, expires, CancellationToken.None)).Allowed.ShouldBeFalse();
        (await store.TryConsumeAsync("b", 1, expires, CancellationToken.None)).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task InMemoryStore_ConcurrentConsumers_NeverExceedLimit()
    {
        var store = new InMemoryQuotaStore();
        var expires = DateTimeOffset.UtcNow.AddDays(1);
        var allowed = 0;

        await Parallel.ForAsync(0, 1000, async (_, ct) =>
        {
            if ((await store.TryConsumeAsync("hot", 50, expires, ct)).Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBe(50);
    }

    [Fact]
    public async Task InMemoryStore_ExpiredCounters_ArePruned()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new InMemoryQuotaStore(time);
        for (var i = 0; i < 100; i++)
        {
            await store.TryConsumeAsync("old" + i, 1, time.GetUtcNow().AddHours(1), CancellationToken.None);
        }

        time.Advance(TimeSpan.FromHours(2));
        for (var i = 0; i < 4100; i++)
        {
            await store.TryConsumeAsync("live", 10, time.GetUtcNow().AddHours(1), CancellationToken.None);
        }

        store.Count.ShouldBe(1);
    }
}
