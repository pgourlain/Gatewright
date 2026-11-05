// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.AdminApi;

public static partial class AdminEndpoints
{
    private static RevisionResponse ToResponse(Revision r) => new(r.Number, r.ConfigHash, r.Summary, r.CreatedBy, r.CreatedAt);

    private static PublicationResponse ToResponse(Publication p, long revisionNumber) =>
        new(p.Number, revisionNumber, p.Kind.ToString(), p.SnapshotHash, p.PublishedBy, p.PublishedAt);

    private static void MapPublishing(RouteGroupBuilder group)
    {
        group.MapGet("/revisions", async (RevisionService revisions, CancellationToken ct, int? take) =>
            (await revisions.ListAsync(Math.Clamp(take ?? 50, 1, 500), ct)).Select(ToResponse))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/revisions/{number:long}", async (long number, RevisionService revisions, CancellationToken ct) =>
            await revisions.GetAsync(number, ct) is { } revision
                ? Results.Json(new { revision = ToResponse(revision), config = System.Text.Json.JsonDocument.Parse(revision.ConfigJson).RootElement })
                : RequestValidation.NotFound("Revision", number.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/revisions/{from:long}/diff/{to:long}", async (long from, long to, RevisionService revisions, CancellationToken ct) =>
            await revisions.DiffAsync(from, to, ct) is { } diff
                ? Results.Ok(diff.Select(d => new { kind = d.Kind.ToString(), path = d.Path, before = d.Before, after = d.After }))
                : RequestValidation.NotFound("Revision", $"{from} or {to}"))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/environments", async (GatewrightDbContext db, CancellationToken ct) =>
        {
            var environments = await db.Environments.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct);
            var publications = await db.Publications.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
            return environments.Select(e => new
            {
                name = e.Name,
                currentPublication = e.CurrentPublicationId is { } id && publications.TryGetValue(id, out var p) ? (long?)p.Number : null,
            });
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/environments/{env}/publications", async (string env, GatewrightDbContext db, RevisionService revisions, CancellationToken ct) =>
        {
            if (!await db.Environments.AnyAsync(e => e.Name == env, ct))
            {
                return RequestValidation.NotFound("Environment", env);
            }

            var publications = await revisions.ListPublicationsAsync(env, ct);
            var numbers = await db.Revisions.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Number, ct);
            return Results.Ok(publications.Select(p => ToResponse(p, numbers[p.RevisionId])));
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/environments/{env}/current", async (string env, GatewrightDbContext db, RevisionService revisions, CancellationToken ct) =>
        {
            if (!await db.Environments.AnyAsync(e => e.Name == env, ct))
            {
                return RequestValidation.NotFound("Environment", env);
            }

            return await revisions.GetCurrentAsync(env, ct) is { } publication
                ? Results.Content(publication.SnapshotJson, "application/json")
                : Results.NoContent();
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/environments/{env}/publications", (string env, PublishRequest request, RevisionService revisions, ChangeRecorder recorder, ActorAccessor actor, CancellationToken ct) =>
            ActivateAsync(env, request, PublicationKind.Publish, revisions, recorder, actor, ct))
            .RequireAuthorization(AdminRoles.PublisherPolicy);

        group.MapPost("/environments/{env}/rollback", (string env, PublishRequest request, RevisionService revisions, ChangeRecorder recorder, ActorAccessor actor, CancellationToken ct) =>
            ActivateAsync(env, request, PublicationKind.Rollback, revisions, recorder, actor, ct))
            .RequireAuthorization(AdminRoles.PublisherPolicy);

        group.MapGet("/nodes", async (GatewrightDbContext db, RevisionService revisions, CancellationToken ct, string? environment) =>
        {
            var nodes = await db.Nodes.AsNoTracking()
                .Where(n => environment == null || n.Environment == environment)
                .OrderBy(n => n.Environment).ThenBy(n => n.NodeId)
                .ToListAsync(ct);
            var latest = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in nodes.Select(n => n.Environment).Distinct())
            {
                latest[name] = (await revisions.GetCurrentAsync(name, ct))?.Number ?? 0;
            }

            return nodes.Select(n => new NodeResponse(
                n.NodeId,
                n.Environment,
                n.AppliedVersion,
                n.AppliedHash,
                latest[n.Environment],
                n.AppliedVersion == latest[n.Environment],
                n.Connected,
                n.ConnectedAt,
                n.LastSeen,
                n.LastError));
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/audit", async (GatewrightDbContext db, CancellationToken ct, int? take) =>
            (await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Timestamp).Take(Math.Clamp(take ?? 100, 1, 1000)).ToListAsync(ct))
                .Select(a => new AuditResponse(a.Timestamp, a.Actor, a.Action, a.EntityType, a.EntityId, a.Details)))
            .RequireAuthorization(AdminRoles.AdminPolicy);
    }

    private static async Task<IResult> ActivateAsync(
        string env,
        PublishRequest request,
        PublicationKind kind,
        RevisionService revisions,
        ChangeRecorder recorder,
        ActorAccessor actor,
        CancellationToken ct)
    {
        var number = request.Revision;
        if (number <= 0)
        {
            if (kind == PublicationKind.Rollback)
            {
                return RequestValidation.Problem("revision", "A revision number is required for a rollback.");
            }

            var (latest, _) = await revisions.CaptureDraftAsync(actor.Current, "publish latest draft", ct);
            number = latest.Number;
        }

        var result = kind == PublicationKind.Publish
            ? await revisions.PublishAsync(number, env, actor.Current, ct)
            : await revisions.RollbackAsync(number, env, actor.Current, ct);

        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                PublishFailure.EnvironmentNotFound => RequestValidation.NotFound("Environment", env),
                PublishFailure.RevisionNotFound => RequestValidation.NotFound("Revision", number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                PublishFailure.NeverPublished => RequestValidation.Conflict($"Revision {number} was never published to '{env}'."),
                _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["configuration"] = [.. result.Errors] }),
            };
        }

        await recorder.AuditAsync(
            kind == PublicationKind.Publish ? "publish" : "rollback",
            "environment",
            env,
            $"revision {number} -> publication {result.Publication!.Number}",
            ct);
        return Results.Ok(ToResponse(result.Publication!, number));
    }
}
