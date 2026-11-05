// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using Gatewright.Core.Hosting;
using Gatewright.Core.Policies;
using Gatewright.DataPlane;
using Gatewright.Policies.Cache;
using Microsoft.Extensions.Options;

namespace Gatewright.Sdk.Internal;

/// <summary>Maps liveness, readiness and diagnostics endpoints.</summary>
internal static class HealthEndpointExtensions
{
    /// <summary>
    /// Maps <c>/healthz</c> (liveness), <c>/readyz</c> (ready once a valid snapshot is applied; always ready for a control plane only process)
    /// and <c>/gw/config</c> (applied version and hash) <c>GET /gw/policies</c> (catalog of policy types and their JSON Schemas) and <c>POST /gw/cache/evict?tag=</c> (all require <c>Authorization: Bearer &lt;Gateway:DiagnosticsToken&gt;</c>; disabled when no token is set).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapGatewrightHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/healthz", () => Results.Text("ok"));

        endpoints.MapGet("/readyz", (IServiceProvider sp) =>
        {
            if (sp.GetService<ReadinessGate>() is { IsDraining: true })
            {
                return Results.Text("shutting down", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var registry = sp.GetService<PipelineRegistry>();
            return registry is null || registry.HasSnapshot
                ? Results.Text("ready")
                : Results.Text("no valid configuration applied yet", statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        endpoints.MapGet("/gw/config", (HttpContext context, IOptions<GatewayOptions> options, IServiceProvider sp) =>
        {
            if (Authorize(context, options.Value) is { } denied)
            {
                return denied;
            }

            var snapshot = sp.GetService<PipelineRegistry>()?.Current;
            return snapshot is null
                ? Results.Json(new { mode = options.Value.Mode.ToString(), applied = false }, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Json(new { mode = options.Value.Mode.ToString(), applied = true, version = snapshot.Version, hash = snapshot.Hash });
        });

        endpoints.MapGet("/gw/policies", (HttpContext context, IOptions<GatewayOptions> options, PolicyRegistry registry) =>
        {
            if (Authorize(context, options.Value) is { } denied)
            {
                return denied;
            }

            return Results.Json(registry.Describe().Select(d => new
            {
                type = d.Type,
                phases = d.Phases.ToString().Split(", "),
                schema = d.Schema,
            }));
        });

        endpoints.MapPost("/gw/cache/evict", async (HttpContext context, IOptions<GatewayOptions> options, CacheInvalidator invalidator, string? tag) =>
        {
            if (Authorize(context, options.Value) is { } denied)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return Results.BadRequest(new { error = "Query parameter 'tag' is required." });
            }

            await invalidator.EvictByTagAsync(tag, context.RequestAborted);
            return Results.NoContent();
        });

        return endpoints;
    }

    private static IResult? Authorize(HttpContext context, GatewayOptions options)
    {
        var token = options.DiagnosticsToken;
        if (string.IsNullOrEmpty(token))
        {
            return Results.NotFound();
        }

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..])),
                SHA256.HashData(Encoding.UTF8.GetBytes(token))))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
