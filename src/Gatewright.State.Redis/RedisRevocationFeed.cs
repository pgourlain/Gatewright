// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Gatewright.Core.Security;
using StackExchange.Redis;

namespace Gatewright.State.Redis;

/// <summary>Key revocations over Redis pub/sub, so every gateway instance stops accepting a key within moments.</summary>
public sealed class RedisRevocationFeed : IRevocationFeed
{
    private readonly RedisGuard _redis;

    /// <summary>Initializes a new instance of the <see cref="RedisRevocationFeed"/> class.</summary>
    /// <param name="redis">The guarded Redis connection.</param>
    public RedisRevocationFeed(RedisGuard redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(string keyHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        await _redis.ExecuteAsync(
            async connection => await connection.GetSubscriber().PublishAsync(RedisChannel.Literal(_redis.KeyPrefix + "revocations"), keyHash).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        ChannelMessageQueue? queue = null;
        while (queue is null)
        {
            try
            {
                queue = await _redis.ExecuteAsync(
                    async connection => await connection.GetSubscriber().SubscribeAsync(RedisChannel.Literal(_redis.KeyPrefix + "revocations")).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RedisUnavailableException)
            {
                // Redis may come back: keep trying, revocations are also covered by the next snapshot.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }

        queue.OnMessage(message =>
        {
            channel.Writer.TryWrite(message.Message.ToString());
            return Task.CompletedTask;
        });

        try
        {
            await foreach (var hash in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return hash;
            }
        }
        finally
        {
            await queue.UnsubscribeAsync().ConfigureAwait(false);
        }
    }
}
