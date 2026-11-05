// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.ApiKey;

/// <summary>Configuration of the <c>apiKey</c> policy.</summary>
public sealed class ApiKeyConfig
{
    /// <summary>Gets or sets the header carrying the key.</summary>
    public string Header { get; set; } = "x-api-key";

    /// <summary>Gets or sets an optional query-string parameter accepted when the header is absent.</summary>
    public string? QueryParameter { get; set; }

    /// <summary>Gets or sets a value indicating whether a key is mandatory.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Gets or sets the products the subscription must belong to; empty accepts any product.</summary>
    public List<string> Products { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the key is removed from the request before it is forwarded to the backend.</summary>
    public bool StripFromRequest { get; set; } = true;
}
