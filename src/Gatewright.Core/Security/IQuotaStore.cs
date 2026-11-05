// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>Counts quota consumption per key and period. The in-memory implementation is local; a strict shared implementation (Redis) arrives later.</summary>
public interface IQuotaStore
{
    /// <summary>Tries to consume one unit.</summary>
    /// <param name="key">Identifies the counter (policy name, partition and period start).</param>
    /// <param name="limit">The quota limit for the period.</param>
    /// <param name="expiresAt">When the counter can be forgotten (end of the period).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    ValueTask<QuotaResult> TryConsumeAsync(string key, long limit, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
