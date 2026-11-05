// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Gatewright.Policies.RateLimit;

/// <summary>Holds the limiters so that policy instances with the same name and settings share counters across snapshots and operations.</summary>
public sealed class RateLimiterRegistry
{
    private readonly ConcurrentDictionary<string, SharedRateLimiter> _shared = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LocalRateLimiter> _limiters = new(StringComparer.Ordinal);

    internal LocalRateLimiter GetOrCreate(string name, bool tokenBucket, int limit, int periodSeconds, int burst, TimeProvider time) =>
        _limiters.GetOrAdd(
            $"{name}|{(tokenBucket ? "tb" : "fw")}|{limit}|{periodSeconds}|{burst}",
            _ => new LocalRateLimiter(tokenBucket, limit, periodSeconds, burst, time));

    internal SharedRateLimiter GetOrCreateShared(
        string name,
        int limit,
        int periodSeconds,
        int burst,
        int? batchSize,
        bool failOpen,
        Gatewright.Core.Security.IRateLimitStore store,
        TimeProvider time,
        Microsoft.Extensions.Logging.ILogger? logger) =>
        _shared.GetOrAdd(
            $"{name}|{limit}|{periodSeconds}|{burst}|{batchSize}|{failOpen}",
            _ => new SharedRateLimiter(store, name, limit, periodSeconds, burst, batchSize, failOpen, time, logger));
}
