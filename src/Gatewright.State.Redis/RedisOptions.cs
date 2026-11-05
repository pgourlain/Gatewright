// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.State.Redis;

/// <summary>Redis settings, bound from <c>Redis</c>.</summary>
public sealed class RedisOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Redis";

    /// <summary>Gets or sets the StackExchange.Redis connection string. Provide it through a secret store; Redis is disabled when empty.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the prefix of every key and channel written by Gatewright.</summary>
    public string KeyPrefix { get; set; } = "gw:";

    /// <summary>Gets or sets the longest time a Redis call may take before it counts as a failure, in milliseconds. Keep it short: Redis is never worth slowing requests down.</summary>
    public int CommandTimeoutMilliseconds { get; set; } = 100;

    /// <summary>Gets or sets how many consecutive failures open the circuit around Redis.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Gets or sets how long, in seconds, Redis calls are skipped once the circuit is open.</summary>
    public int BreakSeconds { get; set; } = 10;
}
