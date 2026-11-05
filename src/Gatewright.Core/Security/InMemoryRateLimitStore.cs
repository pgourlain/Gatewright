// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Gatewright.Core.Security;

/// <summary>Process-local <see cref="IRateLimitStore"/> (development, tests, single-instance deployments).</summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="InMemoryRateLimitStore"/> class.</summary>
    /// <param name="time">The clock.</param>
    public InMemoryRateLimitStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <inheritdoc/>
    public ValueTask<RateLimitReservation> ReserveAsync(string key, RateLimitBudget budget, int requested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var now = _time.GetTimestamp();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(budget.Capacity, now));
        lock (bucket)
        {
            var elapsed = _time.GetElapsedTime(bucket.Stamp, now).TotalSeconds;
            bucket.Tokens = Math.Min(budget.Capacity, bucket.Tokens + (elapsed * budget.RefillPerSecond));
            bucket.Stamp = now;

            var granted = (int)Math.Min(requested, Math.Floor(bucket.Tokens));
            bucket.Tokens -= granted;
            var retry = granted >= requested
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(((granted == 0 ? 1 : requested - granted) - (bucket.Tokens % 1)) / budget.RefillPerSecond);
            return new ValueTask<RateLimitReservation>(new RateLimitReservation(granted, retry));
        }
    }

    private sealed class Bucket(double tokens, long stamp)
    {
        public double Tokens = tokens;
        public long Stamp = stamp;
    }
}
