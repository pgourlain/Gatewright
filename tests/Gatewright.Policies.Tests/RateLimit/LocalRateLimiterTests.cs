// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Policies.RateLimit;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.Policies.Tests.RateLimit;

public sealed class LocalRateLimiterTests
{
    private readonly FakeTimeProvider _time = new();

    private LocalRateLimiter TokenBucket(int limit = 10, int period = 10, int? burst = null) =>
        new(true, limit, period, burst ?? limit, _time);

    private LocalRateLimiter FixedWindow(int limit = 3, int period = 10) => new(false, limit, period, limit, _time);

    [Fact]
    public void TokenBucket_BurstThenRejected_WithRetryAfter()
    {
        var limiter = TokenBucket(limit: 5, period: 5);

        for (var i = 0; i < 5; i++)
        {
            limiter.Acquire("a").Allowed.ShouldBeTrue();
        }

        var rejected = limiter.Acquire("a");
        rejected.Allowed.ShouldBeFalse();
        rejected.Remaining.ShouldBe(0);
        rejected.RetryAfterSeconds.ShouldBe(1);
    }

    [Fact]
    public void TokenBucket_RefillsOverTime()
    {
        var limiter = TokenBucket(limit: 10, period: 10);
        for (var i = 0; i < 10; i++)
        {
            limiter.Acquire("a");
        }

        limiter.Acquire("a").Allowed.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(3));

        // 1 token per second: three requests fit after three seconds.
        limiter.Acquire("a").Allowed.ShouldBeTrue();
        limiter.Acquire("a").Allowed.ShouldBeTrue();
        limiter.Acquire("a").Allowed.ShouldBeTrue();
        limiter.Acquire("a").Allowed.ShouldBeFalse();
    }

    [Fact]
    public void TokenBucket_NeverExceedsBurstCapacity()
    {
        var limiter = TokenBucket(limit: 10, period: 10, burst: 3);
        _time.Advance(TimeSpan.FromMinutes(5));

        var allowed = Enumerable.Range(0, 10).Count(_ => limiter.Acquire("a").Allowed);

        allowed.ShouldBe(3);
    }

    [Fact]
    public void Partitions_AreIndependent()
    {
        var limiter = TokenBucket(limit: 1, period: 60);

        limiter.Acquire("a").Allowed.ShouldBeTrue();
        limiter.Acquire("a").Allowed.ShouldBeFalse();
        limiter.Acquire("b").Allowed.ShouldBeTrue();
    }

    [Fact]
    public void FixedWindow_AllowsLimitThenRejectsUntilWindowEnds()
    {
        var limiter = FixedWindow(limit: 3, period: 10);

        limiter.Acquire("a").Remaining.ShouldBe(2);
        limiter.Acquire("a");
        limiter.Acquire("a").Remaining.ShouldBe(0);
        var rejected = limiter.Acquire("a");
        rejected.Allowed.ShouldBeFalse();
        rejected.RetryAfterSeconds.ShouldBe(10);

        _time.Advance(TimeSpan.FromSeconds(4));
        limiter.Acquire("a").RetryAfterSeconds.ShouldBe(6);

        _time.Advance(TimeSpan.FromSeconds(6));
        limiter.Acquire("a").Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Acquire_ManyConcurrentCalls_NeverExceedsLimit()
    {
        var limiter = FixedWindow(limit: 100, period: 60);

        var allowed = 0;
        Parallel.For(0, 1000, _ =>
        {
            if (limiter.Acquire("hot").Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBe(100);
    }

    [Fact]
    public void Prune_IdlePartitions_AreForgotten()
    {
        var limiter = TokenBucket(limit: 10, period: 1);
        for (var i = 0; i < 100; i++)
        {
            limiter.Acquire("old-" + i);
        }

        _time.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 5000; i++)
        {
            limiter.Acquire("hot");
        }

        limiter.PartitionCount.ShouldBeLessThan(10);
    }
}
