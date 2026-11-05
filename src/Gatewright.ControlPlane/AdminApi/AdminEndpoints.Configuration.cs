// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gatewright.ControlPlane.AdminApi;

public static partial class AdminEndpoints
{
    private static void MapConfiguration(RouteGroupBuilder group)
    {
        group.MapGet("/draft", async (ConfigExporter exporter, CancellationToken ct) =>
            Results.Content(GatewayConfigSerializer.Serialize(await exporter.ExportAsync(ct)), "application/json"))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/policy-types", (Gatewright.Core.Policies.PolicyRegistry registry) =>
            registry.Describe().Select(d => new
            {
                type = d.Type,
                phases = d.Phases.ToString().Split(", "),
                schema = d.Schema,
            })).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/discovery/services", async (IServiceProvider services, CancellationToken ct) =>
        {
            var store = services.GetService(typeof(Gatewright.Core.Discovery.IDiscoveryStore)) as Gatewright.Core.Discovery.IDiscoveryStore;
            var state = store is null ? Gatewright.Core.Discovery.DiscoveryState.Empty : await store.LoadAsync(ct);
            return state.Services.Select(service => new DiscoveredServiceResponse(
                service.Namespace,
                service.Name,
                [.. service.Ports.Select(port => new DiscoveredPortResponse(port, service.Reference(port), state.Endpoints.For(service.Reference(port)).Count))],
                service.OpenApiPath,
                service.BasePath,
                service.Product));
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/export", async (ConfigPorter porter, string? environment, CancellationToken ct) =>
            await porter.ExportAsync(environment, ct) is { } document
                ? Results.Content(GatewayJson.Serialize(document), "application/json")
                : RequestValidation.NotFound("Publication", environment ?? "draft"))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/import", async (DeclarativeExport document, ConfigPorter porter, ActorAccessor actor, bool? dryRun, CancellationToken ct) =>
        {
            var result = await porter.ImportAsync(document, dryRun ?? false, actor.Current, ct);
            return result.Errors.Count > 0
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [.. result.Errors] })
                : Results.Ok(new { applied = result.Applied, hash = result.Hash });
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPost("/validate", async (ConfigExporter exporter, SnapshotTranslator translator, HttpRequest request, CancellationToken ct) =>
        {
            GatewayConfig config;
            if (request.ContentLength is > 0)
            {
                try
                {
                    using var reader = new StreamReader(request.Body);
                    config = GatewayConfigSerializer.Deserialize(await reader.ReadToEndAsync(ct));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return RequestValidation.Problem("body", ex.Message);
                }
            }
            else
            {
                config = await exporter.ExportAsync(ct);
            }

            var result = translator.Translate(config);
            return Results.Ok(new { valid = result.IsSuccess, errors = result.Errors, hash = result.Snapshot?.Hash });
        }).RequireAuthorization(AdminRoles.ReaderPolicy);
    }
}
