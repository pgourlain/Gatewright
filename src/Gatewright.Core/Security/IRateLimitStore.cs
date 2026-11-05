// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>
/// The global token bucket behind distributed rate limiting. Gateways do not call it for every request: they reserve batches of
/// tokens and spend them locally.
/// </summary>
public interface IRateLimitStore
{
    /// <summary>Takes up to <paramref name="requested"/> tokens from the bucket of <paramref name="key"/>.</summary>
    /// <param name="key">Identifies the bucket (policy name and partition).</param>
    /// <param name="budget">The bucket settings.</param>
    /// <param name="requested">How many tokens the caller wants.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many tokens were granted.</returns>
    ValueTask<RateLimitReservation> ReserveAsync(string key, RateLimitBudget budget, int requested, CancellationToken cancellationToken);
}
