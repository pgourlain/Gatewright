// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Gatewright.Policies.RateLimit;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.Policies.Tests.RateLimit;

public sealed class SharedRateLimiterTests
{
    private sealed class CountingStore(IRateLimitStore inner) : IRateLimitStore
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool Fail { get; set; }

        public ValueTask<RateLimitReservation> ReserveAsync(string key, RateLimitBudget budget, int requested, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Fail ? throw new InvalidOperationException("store down") : inner.ReserveAsync(key, budget, requested, cancellationToken);
        }
    }

    private readonly FakeTimeProvider _time = new();

    private CountingStore NewStore() => new(new InMemoryRateLimitStore(_time));

    private SharedRateLimiter Limiter(IRateLimitStore store, int limit, int period = 60, int? batch = null, bool failOpen = true) =>
        new(store, "test", limit, period, limit, batch, failOpen, _time, null);

    [Fact]
    public async Task ThreeInstances_RespectTheGlobalLimitWithinOneBatchPerInstance()
    {
        var store = NewStore();
        var instances = Enumerable.Range(0, 3).Select(_ => Limiter(store, limit: 100, batch: 10)).ToArray();

        var allowed = 0;
        for (var i = 0; i < 600; i++)
        {
            if ((await instances[i % 3].AcquireAsync("sub", CancellationToken.None)).Allowed)
            {
                allowed++;
            }
        }

        allowed.ShouldBeGreaterThanOrEqualTo(100 - 30);
        allowed.ShouldBeLessThanOrEqualTo(100 + (3 * 10));
    }

    [Fact]
    public async Task NominalLoad_CallsTheStoreLessThanOncePerTwentyRequests()
    {
        var store = NewStore();
        var limiter = Limiter(store, limit: 2000, period: 1);

        for (var i = 0; i < 1000; i++)
        {
            (await limiter.AcquireAsync("sub", CancellationToken.None)).Allowed.ShouldBeTrue();
        }

        store.Calls.ShouldBeLessThan(1000 / 20);
    }

    [Fact]
    public async Task Exhausted_RejectedRequestsDoNotHammerTheStore()
    {
        var store = NewStore();
        var limiter = Limiter(store, limit: 10, batch: 10);
        for (var i = 0; i < 10; i++)
        {
            await limiter.AcquireAsync("sub", CancellationToken.None);
        }

        var callsAfterExhaustion = store.Calls;
        var rejected = 0;
        for (var i = 0; i < 200; i++)
        {
            var decision = await limiter.AcquireAsync("sub", CancellationToken.None);
            if (!decision.Allowed)
            {
                rejected++;
                decision.RetryAfterSeconds.ShouldBeGreaterThan(0);
            }
        }

        rejected.ShouldBe(200);
        (store.Calls - callsAfterExhaustion).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task AfterRefill_RequestsAreAllowedAgain()
    {
        var store = NewStore();
        var limiter = Limiter(store, limit: 10, period: 10, batch: 10);
        for (var i = 0; i < 10; i++)
        {
            await limiter.AcquireAsync("sub", CancellationToken.None);
        }

        (await limiter.AcquireAsync("sub", CancellationToken.None)).Allowed.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(5));

        (await limiter.AcquireAsync("sub", CancellationToken.None)).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task UnusedLocalTokens_ExpireSoOtherInstancesCanUseTheBudget()
    {
        var store = NewStore();
        var idle = Limiter(store, limit: 10, period: 600, batch: 10);
        var busy = Limiter(store, limit: 10, period: 600, batch: 10);
        await idle.AcquireAsync("sub", CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(2));

        // The idle instance still holds 9 tokens locally but they expired from its stock; its next call must go back to the store.
        var callsBefore = store.Calls;
        await idle.AcquireAsync("sub", CancellationToken.None);
        store.Calls.ShouldBe(callsBefore + 1);
        (await busy.AcquireAsync("sub", CancellationToken.None)).Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task PartitionsAreIndependent()
    {
        var store = NewStore();
        var limiter = Limiter(store, limit: 1, batch: 1);

        (await limiter.AcquireAsync("a", CancellationToken.None)).Allowed.ShouldBeTrue();
        (await limiter.AcquireAsync("a", CancellationToken.None)).Allowed.ShouldBeFalse();
        (await limiter.AcquireAsync("b", CancellationToken.None)).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task StoreDown_FailOpen_AllowsAndBacksOffFromTheStore()
    {
        var store = NewStore();
        store.Fail = true;
        var limiter = Limiter(store, limit: 5, failOpen: true);

        for (var i = 0; i < 50; i++)
        {
            var decision = await limiter.AcquireAsync("sub", CancellationToken.None);
            decision.Allowed.ShouldBeTrue();
            decision.StoreUnavailable.ShouldBeTrue();
        }

        store.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task StoreDown_FailClosed_RejectsAsUnavailable()
    {
        var store = NewStore();
        store.Fail = true;
        var limiter = Limiter(store, limit: 5, failOpen: false);

        var decision = await limiter.AcquireAsync("sub", CancellationToken.None);

        decision.Allowed.ShouldBeFalse();
        decision.StoreUnavailable.ShouldBeTrue();
    }

    [Fact]
    public async Task StoreRecovers_AfterBackoff_UsesTheStoreAgain()
    {
        var store = NewStore();
        store.Fail = true;
        var limiter = Limiter(store, limit: 100, batch: 10);
        await limiter.AcquireAsync("sub", CancellationToken.None);
        store.Fail = false;
        _time.Advance(TimeSpan.FromSeconds(2));

        var decision = await limiter.AcquireAsync("sub", CancellationToken.None);

        decision.StoreUnavailable.ShouldBeFalse();
        decision.Allowed.ShouldBeTrue();
        decision.Remaining.ShouldBe(9);
    }

    [Fact]
    public async Task Concurrency_NeverGrantsMoreThanLimitPlusBatchPerInstance()
    {
        var store = NewStore();
        var limiter = Limiter(store, limit: 200, batch: 20);
        var allowed = 0;

        await Parallel.ForAsync(0, 1000, async (_, ct) =>
        {
            if ((await limiter.AcquireAsync("hot", ct)).Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBeLessThanOrEqualTo(200);
        allowed.ShouldBeGreaterThanOrEqualTo(180);
    }
}
