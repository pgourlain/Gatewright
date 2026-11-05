// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Headers;

/// <summary>Header changes. Values may contain variables such as <c>{subscription.id}</c> or <c>{claim:tid}</c>.</summary>
public sealed class HeaderOperations
{
    /// <summary>Gets or sets headers replaced (or created) with the given value.</summary>
    public Dictionary<string, string>? Set { get; set; }

    /// <summary>Gets or sets headers to which the value is appended.</summary>
    public Dictionary<string, string>? Append { get; set; }

    /// <summary>Gets or sets names of headers to remove.</summary>
    public List<string>? Remove { get; set; }
}
