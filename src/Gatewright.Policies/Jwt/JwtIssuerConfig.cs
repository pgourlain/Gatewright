// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Jwt;

/// <summary>A trusted token issuer.</summary>
public sealed class JwtIssuerConfig
{
    /// <summary>Gets or sets the expected <c>iss</c> value.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>Gets or sets the accepted audiences (<c>aud</c>); one must match.</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>Gets or sets the JWKS address. Provide this or <see cref="MetadataAddress"/>.</summary>
    public string? JwksUri { get; set; }

    /// <summary>Gets or sets the OpenID Connect metadata address (<c>.well-known/openid-configuration</c>).</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Gets or sets how often keys are refreshed, in seconds.</summary>
    public int RefreshIntervalSeconds { get; set; } = 3600;

    /// <summary>Gets or sets the tolerated clock skew, in seconds.</summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>Gets or sets scopes that must all be present in the <c>scp</c> or <c>scope</c> claim.</summary>
    public List<string> RequiredScopes { get; set; } = [];

    /// <summary>Gets or sets claims that must be present; the value is a string, or an array of accepted values.</summary>
    public Dictionary<string, System.Text.Json.JsonElement> RequiredClaims { get; set; } = [];
}
