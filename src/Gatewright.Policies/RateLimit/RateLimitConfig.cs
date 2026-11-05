// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.RateLimit;

/// <summary>Configuration of the <c>rateLimit</c> policy.</summary>
public sealed class RateLimitConfig
{
    /// <summary>Gets or sets the algorithm: <c>tokenBucket</c> (default) or <c>fixedWindow</c>.</summary>
    public string Algorithm { get; set; } = "tokenBucket";

    /// <summary>Gets or sets how many requests are allowed per <see cref="PeriodSeconds"/>.</summary>
    public int Limit { get; set; }

    /// <summary>Gets or sets the period, in seconds, over which <see cref="Limit"/> applies.</summary>
    public int PeriodSeconds { get; set; } = 60;

    /// <summary>Gets or sets the bucket capacity for <c>tokenBucket</c>; defaults to <see cref="Limit"/>.</summary>
    public int? Burst { get; set; }

    /// <summary>
    /// Gets or sets how requests are partitioned: <c>subscription</c> (default, falls back to <c>ip</c>), <c>ip</c>, <c>global</c>,
    /// <c>claim:&lt;name&gt;</c> or <c>header:&lt;name&gt;</c>.
    /// </summary>
    public string PartitionBy { get; set; } = "subscription";

    /// <summary>
    /// Gets or sets a value indicating whether the budget is shared by all gateway instances through the configured store (Redis in production).
    /// Instances reserve batches of tokens, so the global limit can be exceeded by at most one batch per instance.
    /// </summary>
    public bool Shared { get; set; }

    /// <summary>Gets or sets a fixed reservation size for shared limits; adaptive when not set.</summary>
    public int? BatchSize { get; set; }

    /// <summary>Gets or sets what happens when the shared store is unavailable: <c>failOpen</c> (default, requests are allowed) or <c>failClosed</c> (requests get 503).</summary>
    public string OnStoreFailure { get; set; } = "failOpen";

    /// <summary>Gets or sets a name shared by policy instances that must count together (for example a global limit declared once and inherited by every operation). Defaults to the declaration scope.</summary>
    public string? Name { get; set; }
}
