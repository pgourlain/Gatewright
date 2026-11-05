// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.OutputCaching;

namespace Gatewright.Policies.Cache;

/// <summary>Evicts cached responses by tag.</summary>
public sealed class CacheInvalidator
{
    private readonly IOutputCacheStore _store;

    /// <summary>Initializes a new instance of the <see cref="CacheInvalidator"/> class.</summary>
    /// <param name="store">The cache store.</param>
    public CacheInvalidator(IOutputCacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Evicts every entry carrying the tag.</summary>
    /// <param name="tag">The tag, for example <c>api:orders</c>.</param>
    /// <param name="cancellationToken">Cancels the eviction.</param>
    /// <returns>A task completing when the entries are evicted.</returns>
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _store.EvictByTagAsync(tag, cancellationToken);
    }
}
