// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Sdk.Internal;

/// <summary>
/// Adds defensive headers to the responses the gateway itself produces (Admin API, health, diagnostics). Proxied responses are never touched:
/// they belong to the backend, and the <c>headers</c> policy is the place to add headers to them.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] OwnedPrefixes = ["/admin", "/gw", "/healthz", "/readyz"];

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        foreach (var prefix in OwnedPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(static state =>
                {
                    var headers = ((HttpContext)state).Response.Headers;
                    headers.XContentTypeOptions = "nosniff";
                    headers["Cache-Control"] = "no-store";
                    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                    headers["Referrer-Policy"] = "no-referrer";
                    return Task.CompletedTask;
                }, context);
                break;
            }
        }

        return next(context);
    }
}
