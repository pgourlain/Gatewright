// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Cors;

/// <summary>Configuration of the <c>cors</c> policy.</summary>
public sealed class CorsConfig
{
    /// <summary>Gets or sets the allowed origins, exact match (<c>https://app.example.com</c>) or <c>*</c>.</summary>
    public List<string> Origins { get; set; } = [];

    /// <summary>Gets or sets the allowed methods for preflight answers. Defaults to the common verbs.</summary>
    public List<string> Methods { get; set; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>Gets or sets the allowed request headers, or <c>*</c> to echo the requested ones.</summary>
    public List<string> Headers { get; set; } = [];

    /// <summary>Gets or sets the response headers exposed to browsers.</summary>
    public List<string> ExposeHeaders { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether credentials are allowed. Cannot be combined with the <c>*</c> origin.</summary>
    public bool Credentials { get; set; }

    /// <summary>Gets or sets how long, in seconds, browsers may cache the preflight answer.</summary>
    public int MaxAgeSeconds { get; set; } = 600;
}
