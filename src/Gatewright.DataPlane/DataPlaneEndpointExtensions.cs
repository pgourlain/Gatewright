// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Gatewright.DataPlane;

/// <summary>Maps the data plane into the ASP.NET Core pipeline.</summary>
public static class DataPlaneEndpointExtensions
{
    /// <summary>Maps the YARP reverse proxy with the policy middleware.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The convention builder of the proxy endpoints.</returns>
    public static ReverseProxyConventionBuilder MapGatewrightDataPlane(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapReverseProxy(proxy =>
        {
            proxy.UseMiddleware<PolicyMiddleware>();
            proxy.UseLoadBalancing();
            proxy.UsePassiveHealthChecks();
        });
    }
}
