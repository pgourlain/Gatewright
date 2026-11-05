// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.AdminApi;

public static partial class AdminEndpoints
{
    private static async Task<SubscriptionResponse> ToResponseAsync(GatewrightDbContext db, SubscriptionEntity subscription, CancellationToken ct)
    {
        var consumer = await db.Consumers.AsNoTracking().FirstAsync(c => c.Id == subscription.ConsumerId, ct);
        var product = await db.Products.AsNoTracking().Include(p => p.Plans).FirstAsync(p => p.Id == subscription.ProductId, ct);
        var plan = product.Plans.FirstOrDefault(p => p.Id == subscription.PlanId);
        return new SubscriptionResponse(
            subscription.Name,
            consumer.Name,
            product.Name,
            plan?.Name,
            subscription.Active,
            [.. subscription.Keys.OrderBy(k => k.CreatedAt).Select(k => new KeyResponse(k.Label, k.CreatedAt, k.RevokedAt))]);
    }

    private static void MapSubscriptions(RouteGroupBuilder group)
    {
        group.MapGet("/subscriptions", async (GatewrightDbContext db, CancellationToken ct) =>
        {
            var all = await db.Subscriptions.AsNoTracking().Include(s => s.Keys).OrderBy(s => s.Name).ToListAsync(ct);
            var result = new List<SubscriptionResponse>(all.Count);
            foreach (var subscription in all)
            {
                result.Add(await ToResponseAsync(db, subscription, ct));
            }

            return result;
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/subscriptions/{name}", async (string name, GatewrightDbContext db, CancellationToken ct) =>
            await db.Subscriptions.AsNoTracking().Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct) is { } subscription
                ? Results.Ok(await ToResponseAsync(db, subscription, ct))
                : RequestValidation.NotFound("Subscription", name))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/subscriptions", async (SubscriptionRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", request.Name, errors);
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            if (await db.Subscriptions.AnyAsync(s => s.Name == request.Name, ct))
            {
                return RequestValidation.Conflict($"Subscription '{request.Name}' already exists.");
            }

            var (subscription, error) = await BuildSubscriptionAsync(db, new SubscriptionEntity { Name = request.Name }, request, ct);
            if (error is not null)
            {
                return error;
            }

            db.Subscriptions.Add(subscription!);
            await recorder.CommitAsync("subscription.create", "subscription", request.Name, ct);
            return Results.Created($"{Prefix}/subscriptions/{request.Name}", await ToResponseAsync(db, subscription!, ct));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPut("/subscriptions/{name}", async (string name, SubscriptionRequest request, GatewrightDbContext db, ChangeRecorder recorder, RevocationPublisher revocations, CancellationToken ct) =>
        {
            var existing = await db.Subscriptions.Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct);
            if (existing is null)
            {
                return RequestValidation.NotFound("Subscription", name);
            }

            var deactivating = existing.Active && !request.Active;

            var (subscription, error) = await BuildSubscriptionAsync(db, existing, request, ct);
            if (error is not null)
            {
                return error;
            }

            await recorder.CommitAsync("subscription.update", "subscription", name, ct);
            if (deactivating)
            {
                await revocations.PublishAsync(subscription!.ActiveKeys.Select(k => k.Hash).ToList(), ct);
            }

            return Results.Ok(await ToResponseAsync(db, subscription!, ct));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/subscriptions/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, RevocationPublisher revocations, CancellationToken ct) =>
        {
            var subscription = await db.Subscriptions.Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct);
            if (subscription is null)
            {
                return RequestValidation.NotFound("Subscription", name);
            }

            var hashes = subscription.ActiveKeys.Select(k => k.Hash).ToList();
            db.Subscriptions.Remove(subscription);
            await recorder.CommitAsync("subscription.delete", "subscription", name, ct);
            await revocations.PublishAsync(hashes, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        MapSubscriptionKeys(group);
    }

    private static async Task<(SubscriptionEntity? Subscription, IResult? Error)> BuildSubscriptionAsync(
        GatewrightDbContext db,
        SubscriptionEntity target,
        SubscriptionRequest request,
        CancellationToken ct)
    {
        var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Name == request.Consumer, ct);
        if (consumer is null)
        {
            return (null, RequestValidation.Problem("consumer", $"Consumer '{request.Consumer}' does not exist."));
        }

        var product = await db.Products.Include(p => p.Plans).FirstOrDefaultAsync(p => p.Name == request.Product, ct);
        if (product is null)
        {
            return (null, RequestValidation.Problem("product", $"Product '{request.Product}' does not exist."));
        }

        PlanEntity? plan = null;
        if (request.Plan is not null)
        {
            plan = product.Plans.FirstOrDefault(p => p.Name == request.Plan);
            if (plan is null)
            {
                return (null, RequestValidation.Problem("plan", $"Plan '{request.Plan}' does not exist in product '{request.Product}'."));
            }
        }

        target.ConsumerId = consumer.Id;
        target.ProductId = product.Id;
        target.PlanId = plan?.Id;
        target.Active = request.Active;
        return (target, null);
    }
}
