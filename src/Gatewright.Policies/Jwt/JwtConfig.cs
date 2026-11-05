// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Jwt;

/// <summary>Configuration of the <c>jwt</c> policy.</summary>
public sealed class JwtConfig
{
    /// <summary>Gets or sets a value indicating whether a valid token is mandatory. When false, requests without a token pass anonymously (an invalid token is still rejected).</summary>
    public bool Required { get; set; } = true;

    /// <summary>Gets or sets the trusted issuers.</summary>
    public List<JwtIssuerConfig> Issuers { get; set; } = [];
}
