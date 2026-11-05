// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Data;
using Gatewright.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiKeysTests
{
    private static async Task<AdminApiHost> StartWithSubscriptionAsync()
    {
        var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold" });
        await host.PostAsync("/admin/v1/consumers", new ConsumerRequest { Name = "acme" });
        await host.PostAsync("/admin/v1/subscriptions", new SubscriptionRequest { Name = "acme-gold", Consumer = "acme", Product = "gold" });
        return host;
    }

    private static async Task<string> IssueAsync(AdminApiHost host, string label)
    {
        var response = await host.Client.PostAsync($"/admin/v1/subscriptions/acme-gold/keys/{label}", null);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await AdminApiHost.ReadAsync(response)).GetProperty("key").GetString()!;
    }

    [Fact]
    public async Task CreateKey_ShowsPlaintextOnceAndStoresOnlyTheHash()
    {
        await using var host = await StartWithSubscriptionAsync();

        var key = await IssueAsync(host, "primary");

        key.ShouldStartWith("gw_");
        key.Length.ShouldBeGreaterThan(40);
        var subscription = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/subscriptions/acme-gold"));
        subscription.ToString().ShouldNotContain(key);
        subscription.GetProperty("keys")[0].GetProperty("label").GetString().ShouldBe("primary");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var stored = await db.SubscriptionKeys.SingleAsync();
        stored.Hash.ShouldBe(new ApiKeyHasher("test-pepper").Hash(key));
        stored.Hash.ShouldNotContain(key);
        (await db.Revisions.Select(r => r.ConfigJson).ToListAsync()).ShouldAllBe(json => !json.Contains(key, StringComparison.Ordinal));
        (await db.AuditEntries.Select(a => a.Details).ToListAsync()).ShouldAllBe(d => !d.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportedConfiguration_ContainsHashesOfActiveKeysOnly()
    {
        await using var host = await StartWithSubscriptionAsync();
        var primary = await IssueAsync(host, "primary");
        var secondary = await IssueAsync(host, "secondary");
        await host.Client.DeleteAsync("/admin/v1/subscriptions/acme-gold/keys/secondary");

        var draft = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/draft"));

        var hashes = draft.GetProperty("subscriptions")[0].GetProperty("keyHashes").EnumerateArray().Select(h => h.GetString()).ToList();
        var hasher = new ApiKeyHasher("test-pepper");
        hashes.ShouldBe([hasher.Hash(primary)]);
        hashes.ShouldNotContain(hasher.Hash(secondary));
    }

    [Fact]
    public async Task Rotate_ReplacesTheKeyAndInvalidatesTheOldOne()
    {
        await using var host = await StartWithSubscriptionAsync();
        var old = await IssueAsync(host, "primary");

        var rotated = await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/primary/rotate", null);

        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fresh = (await AdminApiHost.ReadAsync(rotated)).GetProperty("key").GetString()!;
        fresh.ShouldNotBe(old);
        var draft = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/draft"));
        var hashes = draft.GetProperty("subscriptions")[0].GetProperty("keyHashes").EnumerateArray().Select(h => h.GetString()).ToList();
        var hasher = new ApiKeyHasher("test-pepper");
        hashes.ShouldBe([hasher.Hash(fresh)]);
        var keys = (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/subscriptions/acme-gold"))).GetProperty("keys");
        keys.GetArrayLength().ShouldBe(2);
        keys.EnumerateArray().Count(k => k.TryGetProperty("revokedAt", out _)).ShouldBe(1);
    }

    [Fact]
    public async Task Revoke_RemovesTheKeyFromTheConfiguration()
    {
        await using var host = await StartWithSubscriptionAsync();
        await IssueAsync(host, "primary");

        (await host.Client.DeleteAsync("/admin/v1/subscriptions/acme-gold/keys/primary")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var draft = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/draft"));
        draft.GetProperty("subscriptions")[0].GetProperty("keyHashes").GetArrayLength().ShouldBe(0);
        (await host.Client.DeleteAsync("/admin/v1/subscriptions/acme-gold/keys/primary")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateKey_LabelAlreadyActiveOrInvalid_IsRefused()
    {
        await using var host = await StartWithSubscriptionAsync();
        await IssueAsync(host, "primary");

        (await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/primary", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/tertiary", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.Client.PostAsync("/admin/v1/subscriptions/ghost/keys/primary", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task KeyOperations_RequireTheAdminRole()
    {
        await using var host = await StartWithSubscriptionAsync();
        host.Authenticate(AdminRoles.Publisher);

        (await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/primary", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void KeyGenerator_ProducesUniqueUrlSafeKeys()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => KeyGenerator.Create()).ToList();

        keys.Distinct().Count().ShouldBe(100);
        keys.ShouldAllBe(k => k.StartsWith("gw_", StringComparison.Ordinal) && !k.Contains('+', StringComparison.Ordinal) && !k.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevokeRotateDeactivateAndDelete_AnnounceTheRevokedKeysToGateways()
    {
        await using var host = await StartWithSubscriptionAsync();
        var feed = host.Services.GetRequiredService<IRevocationFeed>();
        var hasher = new ApiKeyHasher("test-pepper");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<string>();
        var listener = Task.Run(
            async () =>
            {
                await foreach (var hash in feed.SubscribeAsync(cts.Token))
                {
                    received.Add(hash);
                }
            },
            cts.Token);
        await Task.Delay(100);

        var primary = await IssueAsync(host, "primary");
        var secondary = await IssueAsync(host, "secondary");
        await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/primary/rotate", null);
        await host.Client.DeleteAsync("/admin/v1/subscriptions/acme-gold/keys/secondary");
        await host.PutAsync("/admin/v1/subscriptions/acme-gold", new SubscriptionRequest { Consumer = "acme", Product = "gold", Active = false });
        await Task.Delay(300);
        await cts.CancelAsync();
        await listener.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });

        received.ShouldContain(hasher.Hash(primary));
        received.ShouldContain(hasher.Hash(secondary));
        received.Count.ShouldBeGreaterThanOrEqualTo(3);
    }
}
