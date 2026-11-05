// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Portal.Services;

/// <summary>Portal settings, bound from <c>Portal</c>.</summary>
public sealed class PortalOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Portal";

    /// <summary>Gets or sets the base address of the Admin API (the control plane), for example <c>https://gatewright-cp</c>.</summary>
    public string AdminApiBaseUrl { get; set; } = "http://localhost:8081";

    /// <summary>Gets or sets the base address of the gateway used by the test console.</summary>
    public string GatewayBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Gets or sets the bearer token the gateway accepts for trace requests (<c>Gateway:DiagnosticsToken</c>). Optional.</summary>
    public string? GatewayDiagnosticsToken { get; set; }

    /// <summary>Gets or sets the authentication settings.</summary>
    public PortalAuthOptions Auth { get; set; } = new();
}

/// <summary>Authentication settings of the portal.</summary>
public sealed class PortalAuthOptions
{
    /// <summary>Gets or sets the mode: <c>Oidc</c> (default, Entra ID) or <c>Development</c> (no sign-in, an administrator; only honored in the Development environment).</summary>
    public string Mode { get; set; } = "Oidc";

    /// <summary>Gets or sets the OIDC authority, for example <c>https://login.microsoftonline.com/&lt;tenant&gt;/v2.0</c>.</summary>
    public string? Authority { get; set; }

    /// <summary>Gets or sets the application (client) id of the portal.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the client secret. Provide it through a secret store, never a committed file.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets the scope requested for the Admin API, for example <c>api://gatewright-admin/.default</c>.</summary>
    public string Scope { get; set; } = "";

    /// <summary>Gets or sets the claim carrying the roles.</summary>
    public string RoleClaim { get; set; } = "roles";
}
