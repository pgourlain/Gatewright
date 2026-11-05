// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Quota;

/// <summary>Configuration of the <c>quota</c> policy.</summary>
public sealed class QuotaConfig
{
    /// <summary>Gets or sets the number of requests allowed per period.</summary>
    public long Limit { get; set; }

    /// <summary>Gets or sets the calendar period (UTC): <c>hour</c>, <c>day</c> or <c>month</c>.</summary>
    public string Period { get; set; } = "month";

    /// <summary>Gets or sets a name shared by policy instances that count together. Defaults to the declaration scope.</summary>
    public string? Name { get; set; }
}
