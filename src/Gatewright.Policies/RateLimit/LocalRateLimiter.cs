// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Gatewright.Policies.RateLimit;

/// <summary>An in-process rate limiter with one budget per partition key. Thread-safe.</summary>
internal sealed class LocalRateLimiter
{
    private const int PruneEvery = 4096;

    private readonly bool _tokenBucket;
    private readonly int _limit;
    private readonly double _periodSeconds;
    private readonly double _capacity;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private int _sinceLastPrune;

    public LocalRateLimiter(bool tokenBucket, int limit, int periodSeconds, int burst, TimeProvider time)
    {
        _tokenBucket = tokenBucket;
        _limit = limit;
        _periodSeconds = periodSeconds;
        _capacity = burst;
        _time = time;
    }

    public int PartitionCount => _buckets.Count;

    public RateLimitDecision Acquire(string partition)
    {
        var now = _time.GetTimestamp();
        var bucket = _buckets.GetOrAdd(partition, _ => new Bucket(_tokenBucket ? _capacity : 0, now));
        var decision = _tokenBucket ? AcquireTokenBucket(bucket, now) : AcquireFixedWindow(bucket, now);

        if (Interlocked.Increment(ref _sinceLastPrune) >= PruneEvery)
        {
            Interlocked.Exchange(ref _sinceLastPrune, 0);
            Prune(now);
        }

        return decision;
    }

    private RateLimitDecision AcquireTokenBucket(Bucket bucket, long now)
    {
        var ratePerSecond = _limit / _periodSeconds;
        lock (bucket)
        {
            var elapsed = _time.GetElapsedTime(bucket.Stamp, now).TotalSeconds;
            bucket.Value = Math.Min(_capacity, bucket.Value + (elapsed * ratePerSecond));
            bucket.Stamp = now;

            if (bucket.Value >= 1)
            {
                bucket.Value -= 1;
                return new RateLimitDecision(true, _limit, (int)Math.Floor(bucket.Value), CeilSeconds((_capacity - bucket.Value) / ratePerSecond), 0);
            }

            var retry = CeilSeconds((1 - bucket.Value) / ratePerSecond);
            return new RateLimitDecision(false, _limit, 0, CeilSeconds((_capacity - bucket.Value) / ratePerSecond), Math.Max(1, retry));
        }
    }

    private RateLimitDecision AcquireFixedWindow(Bucket bucket, long now)
    {
        lock (bucket)
        {
            // Value holds the number of requests counted in the window that started at Stamp.
            if (bucket.Value == 0 || _time.GetElapsedTime(bucket.Stamp, now).TotalSeconds >= _periodSeconds)
            {
                bucket.Value = 0;
                bucket.Stamp = now;
            }

            var resetIn = CeilSeconds(_periodSeconds - _time.GetElapsedTime(bucket.Stamp, now).TotalSeconds);
            if (bucket.Value < _limit)
            {
                bucket.Value += 1;
                return new RateLimitDecision(true, _limit, _limit - (int)bucket.Value, resetIn, 0);
            }

            return new RateLimitDecision(false, _limit, 0, resetIn, Math.Max(1, resetIn));
        }
    }

    private void Prune(long now)
    {
        foreach (var (key, bucket) in _buckets)
        {
            // A bucket idle for more than two periods is full (token bucket) or expired (fixed window): forgetting it changes nothing.
            if (_time.GetElapsedTime(Volatile.Read(ref bucket.Stamp), now).TotalSeconds > 2 * _periodSeconds)
            {
                _buckets.TryRemove(key, out _);
            }
        }
    }

    private static int CeilSeconds(double seconds) => (int)Math.Ceiling(Math.Max(0, seconds));

    private sealed class Bucket(double value, long stamp)
    {
        public double Value = value;
        public long Stamp = stamp;
    }
}
