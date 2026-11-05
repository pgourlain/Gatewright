// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Headers;

/// <summary>Configuration of the <c>headers</c> policy.</summary>
public sealed class HeadersConfig
{
    /// <summary>Gets or sets the operations applied to the request (Inbound phase).</summary>
    public HeaderOperations? Request { get; set; }

    /// <summary>Gets or sets the operations applied to the response (Outbound phase).</summary>
    public HeaderOperations? Response { get; set; }
}
