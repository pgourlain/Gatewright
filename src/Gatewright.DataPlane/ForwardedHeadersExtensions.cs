// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.DataPlane;

/// <summary>Trusts <c>X-Forwarded-*</c> headers only from configured proxies (Front Door, Application Gateway, an ingress), so client IPs and schemes cannot be spoofed.</summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>The configuration key listing trusted proxies (IP addresses or CIDR ranges).</summary>
    public const string TrustedProxiesKey = "Gateway:ForwardedHeaders:TrustedProxies";

    /// <summary>Registers forwarded headers processing when trusted proxies are configured.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><see langword="true"/> when at least one trusted proxy is configured.</returns>
    public static bool AddGatewrightForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var proxies = configuration.GetSection(TrustedProxiesKey).Get<string[]>() ?? [];
        if (proxies.Length == 0)
        {
            return false;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = null;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
            {
                if (proxy.Contains('/', StringComparison.Ordinal))
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
                }
                else
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            }
        });
        return true;
    }

    /// <summary>Adds the forwarded headers middleware.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static IApplicationBuilder UseGatewrightForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseForwardedHeaders();
    }
}
