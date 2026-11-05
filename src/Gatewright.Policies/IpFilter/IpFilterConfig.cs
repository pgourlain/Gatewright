// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.IpFilter;

/// <summary>Configuration of the <c>ipFilter</c> policy. Entries are IP addresses or CIDR ranges.</summary>
public sealed class IpFilterConfig
{
    /// <summary>Gets or sets the allowed ranges. When not empty, any other client is rejected.</summary>
    public List<string> Allow { get; set; } = [];

    /// <summary>Gets or sets the denied ranges. Deny wins over allow.</summary>
    public List<string> Deny { get; set; } = [];

    /// <summary>
    /// Gets or sets the proxies whose <c>X-Forwarded-For</c> header is trusted. The header is ignored when the direct peer is not in this list.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];
}
