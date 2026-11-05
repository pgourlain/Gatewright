// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Threading.Channels;
using Gatewright.Core.Distribution;
using Gatewright.Core.Snapshots;

namespace Gatewright.Distribution;

/// <summary>In-memory distribution for mode <c>All</c>: a one-slot channel where the latest snapshot replaces an unconsumed one. Supports a single subscriber.</summary>
public sealed class InProcessConfigDistributor : IConfigDistributor
{
    private readonly Channel<ConfigSnapshot> _channel = Channel.CreateBounded<ConfigSnapshot>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <inheritdoc/>
    public ValueTask PublishAsync(ConfigSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _channel.Writer.WriteAsync(snapshot, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ConfigSnapshot> SubscribeAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
