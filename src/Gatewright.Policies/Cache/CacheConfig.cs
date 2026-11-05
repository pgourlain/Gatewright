// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Cache;

/// <summary>Configuration of the <c>cache</c> policy.</summary>
public sealed class CacheConfig
{
    /// <summary>Gets or sets how long responses are cached, in seconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Gets or sets the query parameters that vary the cache key; <c>*</c> (default) means all of them, an empty list means none.</summary>
    public List<string> VaryByQuery { get; set; } = ["*"];

    /// <summary>Gets or sets the request headers that vary the cache key.</summary>
    public List<string> VaryByHeader { get; set; } = [];

    /// <summary>Gets or sets extra tags for invalidation. Tags <c>api:&lt;name&gt;</c> and <c>operation:&lt;api&gt;/&lt;operation&gt;</c> are always added.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether requests carrying credentials (<c>Authorization</c> header or validated identity) may be cached.
    /// Off by default so that a user's response is never served to another user; add the credentials header to <see cref="VaryByHeader"/> when enabling.
    /// </summary>
    public bool AllowAuthenticated { get; set; }

    /// <summary>Gets or sets the largest response body that is cached, in bytes.</summary>
    public int MaxBodyBytes { get; set; } = 1_048_576;
}
