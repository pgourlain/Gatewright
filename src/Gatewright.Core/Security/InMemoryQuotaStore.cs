// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Gatewright.Core.Security;

/// <summary>Process-local quota counters. Expired counters are pruned opportunistically.</summary>
public sealed class InMemoryQuotaStore : IQuotaStore
{
    private const int PruneEvery = 4096;

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private int _sincePrune;

    /// <summary>Initializes a new instance of the <see cref="InMemoryQuotaStore"/> class.</summary>
    /// <param name="time">The clock.</param>
    public InMemoryQuotaStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Gets the number of live counters.</summary>
    public int Count => _counters.Count;

    /// <inheritdoc/>
    public ValueTask<QuotaResult> TryConsumeAsync(string key, long limit, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var counter = _counters.GetOrAdd(key, _ => new Counter(expiresAt));
        QuotaResult result;
        lock (counter)
        {
            if (counter.Used < limit)
            {
                counter.Used++;
                result = new QuotaResult(true, counter.Used, limit);
            }
            else
            {
                result = new QuotaResult(false, counter.Used, limit);
            }
        }

        if (Interlocked.Increment(ref _sincePrune) >= PruneEvery)
        {
            Interlocked.Exchange(ref _sincePrune, 0);
            var now = _time.GetUtcNow();
            foreach (var (k, c) in _counters)
            {
                if (c.ExpiresAt <= now)
                {
                    _counters.TryRemove(k, out _);
                }
            }
        }

        return new ValueTask<QuotaResult>(result);
    }

    private sealed class Counter(DateTimeOffset expiresAt)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public long Used { get; set; }
    }
}
