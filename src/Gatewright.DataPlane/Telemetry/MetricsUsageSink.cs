// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gatewright.DataPlane.Telemetry;

/// <summary>Usage sink that publishes aggregated usage as OpenTelemetry metrics (exported through OTLP when configured).</summary>
public sealed class MetricsUsageSink : IUsageSink
{
    private static readonly Meter Meter = new("Gatewright.Usage");

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("gatewright.usage.requests", description: "Requests per window, by subscription.");

    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("gatewright.usage.errors", description: "Failed requests per window, by subscription.");

    /// <inheritdoc/>
    public string Name => "otlp";

    /// <inheritdoc/>
    public Task WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            var tags = new TagList
            {
                { "api", record.Api },
                { "operation", record.Operation },
                { "product", record.Product },
                { "subscription", record.Subscription },
            };
            Requests.Add(record.Requests, tags);
            Errors.Add(record.Errors, tags);
        }

        return Task.CompletedTask;
    }
}
