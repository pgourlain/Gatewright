// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Mock;

/// <summary>Configuration of the <c>mockResponse</c> policy.</summary>
public sealed class MockResponseConfig
{
    /// <summary>Gets or sets the HTTP status code.</summary>
    public int Status { get; set; } = 200;

    /// <summary>Gets or sets the response headers.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Gets or sets the content type of the body.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Gets or sets the response body, sent as UTF-8.</summary>
    public string? Body { get; set; }
}
