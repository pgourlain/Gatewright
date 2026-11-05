// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Gatewright.Core.Security;
using Microsoft.Extensions.Logging;

namespace Gatewright.Policies.RateLimit;

/// <summary>
/// A rate limiter whose budget is shared by all gateway instances through an <see cref="IRateLimitStore"/>. Each instance reserves batches
/// of tokens and spends them locally, so the store is called far less than once per request. The global limit can be exceeded by at most
/// one batch per instance (the documented tolerance), because reserved tokens are spent locally.
/// </summary>
internal sealed partial class SharedRateLimiter
{
    private static readonly TimeSpan StockLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(1);

    private readonly IRateLimitStore _store;
    private readonly string _name;
    private readonly RateLimitBudget _budget;
    private readonly int _limit;
    private readonly int? _fixedBatch;
    private readonly bool _failOpen;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, Partition> _partitions = new(StringComparer.Ordinal);

    public SharedRateLimiter(
        IRateLimitStore store,
        string name,
        int limit,
        int periodSeconds,
        int capacity,
        int? fixedBatch,
        bool failOpen,
        TimeProvider time,
        ILogger? logger)
    {
        _store = store;
        _name = name;
        _limit = limit;
        _budget = new RateLimitBudget(capacity, (double)limit / periodSeconds);
        _fixedBatch = fixedBatch;
        _failOpen = failOpen;
        _time = time;
        _logger = logger;
    }

    public async ValueTask<RateLimitDecision> AcquireAsync(string partitionKey, CancellationToken cancellationToken)
    {
        var partition = _partitions.GetOrAdd(partitionKey, _ => new Partition());
        var now = _time.GetTimestamp();

        if (TryTakeLocal(partition, now, out var remaining))
        {
            return Granted(partition, now, remaining);
        }

        await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _time.GetTimestamp();
            if (TryTakeLocal(partition, now, out remaining))
            {
                return Granted(partition, now, remaining);
            }

            if (now < partition.BlockedUntil)
            {
                return partition.BlockedByFailure
                    ? FailureDecision()
                    : new RateLimitDecision(false, _limit, 0, 0, Math.Max(1, (int)Math.Ceiling(Seconds(partition.BlockedUntil - now))));
            }

            return await ReserveAsync(partition, partitionKey, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            partition.Gate.Release();
        }
    }

    private async ValueTask<RateLimitDecision> ReserveAsync(Partition partition, string partitionKey, long now, CancellationToken cancellationToken)
    {
        var batch = NextBatch(partition, now);
        RateLimitReservation reservation;
        try
        {
            reservation = await _store.ReserveAsync($"{_name}|{partitionKey}", _budget, batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_logger is not null)
            {
                LogStoreUnavailable(_logger, ex, _name, _failOpen ? "open" : "closed");
            }

            partition.BlockedUntil = now + _time.TimestampFrequency;
            partition.BlockedByFailure = true;
            return FailureDecision();
        }

        partition.BlockedByFailure = false;
        partition.LastReservationStamp = now;
        partition.LastBatch = batch;
        partition.ConsumedSinceReservation = 0;
        if (reservation.Granted <= 0)
        {
            var wait = reservation.RetryAfter < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : reservation.RetryAfter;
            partition.BlockedUntil = now + (long)(wait.TotalSeconds * _time.TimestampFrequency);
            return new RateLimitDecision(false, _limit, 0, 0, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
        }

        // One token is spent by the request that triggered the reservation.
        Volatile.Write(ref partition.StockStamp, now);
        Volatile.Write(ref partition.Stock, reservation.Granted - 1);
        partition.ConsumedSinceReservation = 1;
        return new RateLimitDecision(true, _limit, reservation.Granted - 1, 0, 0);
    }

    private RateLimitDecision Granted(Partition partition, long now, int remaining) =>
        partition.BlockedByFailure && now < partition.BlockedUntil
            ? FailureDecision()
            : new RateLimitDecision(true, _limit, remaining, 0, 0);

    private RateLimitDecision FailureDecision() => _failOpen
        ? new RateLimitDecision(true, _limit, _limit, 0, 0, StoreUnavailable: true)
        : new RateLimitDecision(false, _limit, 0, 0, 1, StoreUnavailable: true);

    private bool TryTakeLocal(Partition partition, long now, out int remaining)
    {
        if (now < partition.BlockedUntil && partition.BlockedByFailure && _failOpen)
        {
            remaining = _limit;
            return true;
        }

        if (_time.GetElapsedTime(Volatile.Read(ref partition.StockStamp), now) > StockLifetime)
        {
            Interlocked.Exchange(ref partition.Stock, 0);
        }

        while (true)
        {
            var stock = Volatile.Read(ref partition.Stock);
            if (stock <= 0)
            {
                remaining = 0;
                return false;
            }

            if (Interlocked.CompareExchange(ref partition.Stock, stock - 1, stock) == stock)
            {
                Interlocked.Increment(ref partition.ConsumedSinceReservation);
                remaining = stock - 1;
                return true;
            }
        }
    }

    private int NextBatch(Partition partition, long now)
    {
        var max = Math.Max(1, _budget.Capacity / 4);
        if (_fixedBatch is { } fixedBatch)
        {
            return Math.Clamp(fixedBatch, 1, Math.Max(1, _budget.Capacity));
        }

        if (partition.LastReservationStamp == 0)
        {
            return Math.Clamp((int)Math.Ceiling(_budget.RefillPerSecond / 10), 1, max);
        }

        // Demand observed since the previous reservation, extrapolated to half a second of traffic.
        var elapsed = Math.Max(0.05, Seconds(now - partition.LastReservationStamp));
        var demandPerSecond = Volatile.Read(ref partition.ConsumedSinceReservation) / elapsed;
        return Math.Clamp((int)Math.Ceiling(demandPerSecond * 0.5), 1, max);
    }

    private double Seconds(long ticks) => (double)ticks / _time.TimestampFrequency;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared rate limit store unavailable for {Name}; failing {Mode}.")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception, string name, string mode);

    private sealed class Partition : IDisposable
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Stock;
        public long StockStamp;
        public long BlockedUntil;
        public bool BlockedByFailure;
        public long LastReservationStamp;
        public int LastBatch;
        public int ConsumedSinceReservation;

        public void Dispose() => Gate.Dispose();
    }
}
