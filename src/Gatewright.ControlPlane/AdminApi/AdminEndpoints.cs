// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Maps the versioned Admin API under <c>/admin/v1</c>.</summary>
public static partial class AdminEndpoints
{
    /// <summary>The route prefix of the Admin API.</summary>
    public const string Prefix = "/admin/v1";

    /// <summary>Maps every Admin API endpoint, each protected by the role policy that fits it.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(Prefix).WithTags("Admin API");
        MapCatalog(group);
        MapSubscriptions(group);
        MapPublishing(group);
        MapConfiguration(group);
        endpoints.MapOpenApi(Prefix + "/openapi.json").RequireAuthorization(AdminRoles.ReaderPolicy);
        return endpoints;
    }
}
