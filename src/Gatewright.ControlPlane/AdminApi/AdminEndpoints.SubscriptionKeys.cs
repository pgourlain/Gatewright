// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.AdminApi;

public static partial class AdminEndpoints
{
    private static void MapSubscriptionKeys(RouteGroupBuilder group)
    {
        group.MapPost("/subscriptions/{name}/keys/{label}", async (
            string name,
            string label,
            GatewrightDbContext db,
            ChangeRecorder recorder,
            ApiKeyHasher hasher,
            TimeProvider time,
            CancellationToken ct) =>
        {
            if (label is not (KeyLabels.Primary or KeyLabels.Secondary))
            {
                return RequestValidation.Problem("label", "Label must be 'primary' or 'secondary'.");
            }

            var subscription = await db.Subscriptions.Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct);
            if (subscription is null)
            {
                return RequestValidation.NotFound("Subscription", name);
            }

            var plaintext = KeyGenerator.Create();
            try
            {
                var key = subscription.AddKey(hasher.Hash(plaintext), label, time.GetUtcNow());
                db.SubscriptionKeys.Add(key);
            }
            catch (InvalidOperationException ex)
            {
                return RequestValidation.Conflict(ex.Message);
            }

            await recorder.CommitAsync("subscription.key.create", "subscription", $"{name}/{label}", ct);
            return Results.Created($"{Prefix}/subscriptions/{name}", new KeyIssued(label, plaintext));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPost("/subscriptions/{name}/keys/{label}/rotate", async (
            string name,
            string label,
            GatewrightDbContext db,
            ChangeRecorder recorder,
            ApiKeyHasher hasher,
            TimeProvider time,
            RevocationPublisher revocations,
            CancellationToken ct) =>
        {
            var subscription = await db.Subscriptions.Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct);
            if (subscription is null)
            {
                return RequestValidation.NotFound("Subscription", name);
            }

            var now = time.GetUtcNow();
            var oldHash = subscription.ActiveKeys.FirstOrDefault(k => k.Label == label)?.Hash;
            if (!subscription.RevokeKey(label, now))
            {
                return RequestValidation.NotFound("Key", $"{name}/{label}");
            }

            var plaintext = KeyGenerator.Create();
            db.SubscriptionKeys.Add(subscription.AddKey(hasher.Hash(plaintext), label, now));
            await recorder.CommitAsync("subscription.key.rotate", "subscription", $"{name}/{label}", ct);
            await revocations.PublishAsync([oldHash!], ct);
            return Results.Ok(new KeyIssued(label, plaintext));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/subscriptions/{name}/keys/{label}", async (
            string name,
            string label,
            GatewrightDbContext db,
            ChangeRecorder recorder,
            TimeProvider time,
            RevocationPublisher revocations,
            CancellationToken ct) =>
        {
            var subscription = await db.Subscriptions.Include(s => s.Keys).FirstOrDefaultAsync(s => s.Name == name, ct);
            if (subscription is null)
            {
                return RequestValidation.NotFound("Subscription", name);
            }

            var revokedHash = subscription.ActiveKeys.FirstOrDefault(k => k.Label == label)?.Hash;
            if (!subscription.RevokeKey(label, time.GetUtcNow()))
            {
                return RequestValidation.NotFound("Key", $"{name}/{label}");
            }

            await recorder.CommitAsync("subscription.key.revoke", "subscription", $"{name}/{label}", ct);
            await revocations.PublishAsync([revokedHash!], ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }
}
