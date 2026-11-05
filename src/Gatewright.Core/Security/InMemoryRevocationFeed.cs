// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Gatewright.Core.Security;

/// <summary>Process-local <see cref="IRevocationFeed"/>: every subscriber receives every revocation published after it subscribed.</summary>
public sealed class InMemoryRevocationFeed : IRevocationFeed
{
    private readonly List<Channel<string>> _subscribers = [];

    /// <inheritdoc/>
    public ValueTask PublishAsync(string keyHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        Channel<string>[] targets;
        lock (_subscribers)
        {
            targets = [.. _subscribers];
        }

        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(keyHash);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        lock (_subscribers)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var hash in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return hash;
            }
        }
        finally
        {
            lock (_subscribers)
            {
                _subscribers.Remove(channel);
            }
        }
    }
}
