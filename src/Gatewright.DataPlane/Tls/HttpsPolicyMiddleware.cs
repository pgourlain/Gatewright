// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Tls;

/// <summary>Applies per-hostname HTTPS behavior: redirects plain HTTP requests (308) and adds the HSTS header on HTTPS responses. Operational endpoints are exempt.</summary>
public sealed class HttpsPolicyMiddleware
{
    private static readonly string[] ExemptPrefixes = ["/healthz", "/readyz", "/gw/"];

    private readonly RequestDelegate _next;
    private readonly CertificateStore _store;
    private readonly TlsOptions _options;

    /// <summary>Initializes a new instance of the <see cref="HttpsPolicyMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="store">The store that knows the hostnames of the applied snapshot.</param>
    /// <param name="options">The TLS options.</param>
    public HttpsPolicyMiddleware(RequestDelegate next, CertificateStore store, IOptions<TlsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _store = store;
        _options = options.Value;
    }

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task completing when the request is handled.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path.Value ?? "/";
        if (ExemptPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)) || _store.FindHostname(context.Request.Host.Host) is not { } hostname)
        {
            return _next(context);
        }

        if (!context.Request.IsHttps)
        {
            if (!hostname.RedirectHttpToHttps)
            {
                return _next(context);
            }

            var port = _options.RedirectPort ?? (_options.HttpsPort is > 0 and not 443 ? _options.HttpsPort : 443);
            var authority = port == 443 ? context.Request.Host.Host : $"{context.Request.Host.Host}:{port}";
            context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = $"https://{authority}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            return Task.CompletedTask;
        }

        if (hostname.Hsts is { } hsts)
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (response, value) = ((HttpResponse, string))state;
                    response.Headers["Strict-Transport-Security"] = value;
                    return Task.CompletedTask;
                },
                (context.Response, HstsValue(hsts)));
        }

        return _next(context);
    }

    private static string HstsValue(HstsDefinition hsts) =>
        $"max-age={hsts.MaxAgeSeconds}{(hsts.IncludeSubDomains ? "; includeSubDomains" : "")}{(hsts.Preload ? "; preload" : "")}";
}
