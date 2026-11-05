// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;

namespace Gatewright.State.Redis;

/// <summary>Metrics emitted by the Redis stores.</summary>
public static class RedisTelemetry
{
    /// <summary>The meter name.</summary>
    public const string MeterName = "Gatewright.State.Redis";

    private static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Calls = Meter.CreateCounter<long>("gatewright.redis.calls", description: "Redis calls attempted.");

    internal static readonly Counter<long> Errors = Meter.CreateCounter<long>("gatewright.redis.errors", description: "Redis calls that failed or timed out.");

    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>("gatewright.redis.circuit_rejections", description: "Redis calls skipped because the circuit is open.");
}
