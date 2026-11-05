// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using StackExchange.Redis;

namespace Gatewright.State.Redis;

/// <summary>Strict quota counters in Redis: an atomic check-and-increment, never exceeding the limit, expiring at the end of the period.</summary>
public sealed class RedisQuotaStore : IQuotaStore
{
    private const string Script = """
        local limit = tonumber(ARGV[1])
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current >= limit then
          return { 0, current }
        end
        current = redis.call('INCR', KEYS[1])
        if redis.call('TTL', KEYS[1]) < 0 then
          redis.call('EXPIREAT', KEYS[1], ARGV[2])
        end
        return { 1, current }
        """;

    private readonly RedisGuard _redis;

    /// <summary>Initializes a new instance of the <see cref="RedisQuotaStore"/> class.</summary>
    /// <param name="redis">The guarded Redis connection.</param>
    public RedisQuotaStore(RedisGuard redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    /// <inheritdoc/>
    public async ValueTask<QuotaResult> TryConsumeAsync(string key, long limit, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var result = await _redis.ExecuteAsync(
            async connection => (long[]?)await connection.GetDatabase().ScriptEvaluateAsync(
                Script,
                [new RedisKey(_redis.KeyPrefix + "quota:" + key)],
                [limit, expiresAt.ToUnixTimeSeconds()]).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return new QuotaResult(result![0] == 1, result[1], limit);
    }
}
