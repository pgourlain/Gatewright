// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using StackExchange.Redis;

namespace Gatewright.State.Redis;

/// <summary>
/// A global token bucket in Redis. One atomic Lua script refills the bucket from the Redis clock and hands out up to the requested
/// number of tokens, so instances never disagree about time.
/// </summary>
public sealed class RedisRateLimitStore : IRateLimitStore
{
    private const string Script = """
        local capacity = tonumber(ARGV[1])
        local rate = tonumber(ARGV[2])
        local requested = tonumber(ARGV[3])
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(data[1])
        local ts = tonumber(data[2])
        if tokens == nil then
          tokens = capacity
          ts = now
        end
        local elapsed = math.max(0, now - ts)
        tokens = math.min(capacity, tokens + elapsed * rate / 1000)
        local granted = math.min(requested, math.floor(tokens))
        tokens = tokens - granted
        redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'ts', tostring(now))
        redis.call('PEXPIRE', KEYS[1], math.ceil(capacity / rate * 1000) + 1000)
        local retry = 0
        if granted < requested then
          local missing = requested - granted
          if granted == 0 then missing = 1 end
          retry = math.ceil((missing - (tokens % 1)) / rate * 1000)
        end
        return { granted, retry }
        """;

    private readonly RedisGuard _redis;

    /// <summary>Initializes a new instance of the <see cref="RedisRateLimitStore"/> class.</summary>
    /// <param name="redis">The guarded Redis connection.</param>
    public RedisRateLimitStore(RedisGuard redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    /// <inheritdoc/>
    public async ValueTask<RateLimitReservation> ReserveAsync(string key, RateLimitBudget budget, int requested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var result = await _redis.ExecuteAsync(
            async connection => (long[]?)await connection.GetDatabase().ScriptEvaluateAsync(
                Script,
                [new RedisKey(_redis.KeyPrefix + "rl:" + key)],
                [budget.Capacity, budget.RefillPerSecond, requested]).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return new RateLimitReservation((int)result![0], TimeSpan.FromMilliseconds(result[1]));
    }
}
