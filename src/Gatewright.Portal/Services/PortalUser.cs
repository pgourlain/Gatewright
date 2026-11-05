// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;

namespace Gatewright.Portal.Services;

/// <summary>What the signed-in user may do, derived from the roles in the token. Mirrors the Admin API roles; the API remains the authority.</summary>
public sealed class PortalUser
{
    private PortalUser(string name, bool admin, bool publisher, bool reader)
    {
        Name = name;
        CanEdit = admin;
        CanPublish = admin || publisher;
        CanRead = admin || publisher || reader;
    }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the user can change the catalog (role Admin).</summary>
    public bool CanEdit { get; }

    /// <summary>Gets a value indicating whether the user can publish and roll back (role Publisher or Admin).</summary>
    public bool CanPublish { get; }

    /// <summary>Gets a value indicating whether the user can read anything (any role).</summary>
    public bool CanRead { get; }

    /// <summary>Builds the user from a principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <param name="roleClaim">The role claim type.</param>
    /// <returns>The user.</returns>
    public static PortalUser From(ClaimsPrincipal principal, string roleClaim = "roles")
    {
        ArgumentNullException.ThrowIfNull(principal);
        var roles = principal.Claims.Where(c => c.Type == roleClaim || c.Type == ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PortalUser(
            principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name ?? "anonymous",
            roles.Contains("Admin"),
            roles.Contains("Publisher"),
            roles.Contains("Reader"));
    }
}
