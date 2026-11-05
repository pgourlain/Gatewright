// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Authentication settings of the Admin API, bound from <c>ControlPlane:Auth</c>.</summary>
public sealed class AdminAuthOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ControlPlane:Auth";

    /// <summary>Gets or sets the OIDC authority, for example <c>https://login.microsoftonline.com/&lt;tenant&gt;/v2.0</c> for Entra ID.</summary>
    public string? Authority { get; set; }

    /// <summary>Gets or sets the expected audience (<c>aud</c>), for example <c>api://gatewright-admin</c>.</summary>
    public string? Audience { get; set; }

    /// <summary>Gets or sets the claim carrying the roles (<c>roles</c> for Entra ID app roles).</summary>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>Gets or sets the claim used as the actor name in audit entries.</summary>
    public string ActorClaim { get; set; } = "preferred_username";

    /// <summary>
    /// Gets or sets a value indicating whether every caller is accepted as <c>dev-user</c> with the <c>Admin</c> role. Only honored when the host
    /// environment is <c>Development</c>; startup fails otherwise. For local development only.
    /// </summary>
    public bool DevelopmentBypass { get; set; }
}
