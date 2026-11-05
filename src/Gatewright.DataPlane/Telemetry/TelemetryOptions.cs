// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.DataPlane.Telemetry;

/// <summary>Business metrics, access log and usage analytics settings, bound from <c>Telemetry</c>.</summary>
public sealed class TelemetryOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>Gets or sets a value indicating whether metrics carry the subscription id. Off by default: one series per subscription can overwhelm a metrics backend.</summary>
    public bool SubscriptionDimension { get; set; }

    /// <summary>Gets or sets the access log settings.</summary>
    public AccessLogOptions AccessLog { get; set; } = new();

    /// <summary>Gets or sets the usage analytics settings.</summary>
    public UsageOptions Usage { get; set; } = new();
}

/// <summary>Access log settings.</summary>
public sealed class AccessLogOptions
{
    /// <summary>Gets or sets a value indicating whether access logs are written.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the fraction of successful requests that are logged (0 to 1). Errors (status 400 and above) are always logged.</summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>Gets or sets the capacity of the in-memory queue. When full, entries are dropped (never blocking requests) and counted.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Gets or sets additional query parameter names whose values are masked, on top of the built-in sensitive names.</summary>
    public List<string> MaskedQueryParameters { get; set; } = [];
}

/// <summary>Usage analytics settings.</summary>
public sealed class UsageOptions
{
    /// <summary>Gets or sets a value indicating whether usage is aggregated and sent to the sinks.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how often aggregated records are sent, in seconds.</summary>
    public int FlushSeconds { get; set; } = 60;
}
