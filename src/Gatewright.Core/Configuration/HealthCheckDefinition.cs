// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>Health checking of a backend's destinations.</summary>
public sealed record HealthCheckDefinition
{
    /// <summary>Gets the active checks (the gateway probes each destination).</summary>
    public ActiveHealthCheckDefinition? Active { get; init; }

    /// <summary>Gets the passive checks (destinations are marked unhealthy from observed failures).</summary>
    public PassiveHealthCheckDefinition? Passive { get; init; }
}

/// <summary>Active probing of destinations.</summary>
public sealed record ActiveHealthCheckDefinition
{
    /// <summary>Gets a value indicating whether probing is on.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets the delay between probes.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the probe timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the path probed on each destination.</summary>
    public string Path { get; init; } = "/health";
}

/// <summary>Passive detection of failing destinations.</summary>
public sealed record PassiveHealthCheckDefinition
{
    /// <summary>Gets a value indicating whether passive checks are on.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets how long a destination marked unhealthy is left alone before being tried again.</summary>
    public TimeSpan ReactivationPeriod { get; init; } = TimeSpan.FromSeconds(60);
}
