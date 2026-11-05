// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.DataPlane.Telemetry;

/// <summary>Aggregated usage of one API, operation and subscription over a window, ready to be billed or analyzed.</summary>
/// <param name="WindowStart">Start of the window (UTC).</param>
/// <param name="WindowEnd">End of the window (UTC).</param>
/// <param name="Api">The API name.</param>
/// <param name="Operation">The operation name, or empty.</param>
/// <param name="Product">The product, or empty.</param>
/// <param name="Subscription">The subscription id, or empty for anonymous calls.</param>
/// <param name="Requests">Requests in the window.</param>
/// <param name="Errors">Requests that ended with status 400 or above.</param>
/// <param name="TotalDurationMs">Sum of request durations in milliseconds.</param>
public sealed record UsageRecord(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Api,
    string Operation,
    string Product,
    string Subscription,
    long Requests,
    long Errors,
    double TotalDurationMs);

/// <summary>Receives aggregated usage in batches. Implementations must not block requests: they are called from a background service.</summary>
public interface IUsageSink
{
    /// <summary>Gets a name for logs.</summary>
    string Name { get; }

    /// <summary>Writes a batch.</summary>
    /// <param name="records">The aggregated records.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completing when the batch is written.</returns>
    Task WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken);
}
