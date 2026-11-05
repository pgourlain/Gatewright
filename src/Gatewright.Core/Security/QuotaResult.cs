// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>The outcome of consuming one unit of a quota.</summary>
/// <param name="Allowed">Whether the unit was granted.</param>
/// <param name="Used">Units consumed in the period, including this one when allowed.</param>
/// <param name="Limit">The quota limit.</param>
public readonly record struct QuotaResult(bool Allowed, long Used, long Limit)
{
    /// <summary>Gets the units left in the period.</summary>
    public long Remaining => Math.Max(0, Limit - Used);
}
