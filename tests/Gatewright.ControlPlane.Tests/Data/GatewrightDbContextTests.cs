// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Tests.Data;

public sealed class GatewrightDbContextTests
{
    [Fact]
    public async Task Initialize_Twice_SeedsDefaultEnvironmentsOnce()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();

        await ControlPlaneDatabase.InitializeAsync(context, seedSample: false, CancellationToken.None);

        (await context.Environments.Select(e => e.Name).OrderBy(n => n).ToListAsync()).ShouldBe(["dev", "prod", "staging"]);
        (await context.ScopePolicies.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Initialize_WithSample_AddsOrdersApiAndProductOnce()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();

        await ControlPlaneDatabase.InitializeAsync(context, seedSample: true, CancellationToken.None);
        await ControlPlaneDatabase.InitializeAsync(context, seedSample: true, CancellationToken.None);

        var api = await context.Apis.Include(a => a.Products).SingleAsync();
        api.Name.ShouldBe("orders");
        api.Products.ShouldHaveSingleItem().Name.ShouldBe("starter");
    }

    [Fact]
    public async Task Api_WithOperationsBackendAndPolicies_RoundTrips()
    {
        using var db = new TestDatabase();
        var apiId = Guid.NewGuid();
        await using (var context = db.CreateContext())
        {
            var api = new ApiEntity
            {
                Id = apiId,
                Name = "orders",
                BasePath = "/orders",
                Backend = new BackendDefinition { Destinations = ["http://a"], Timeout = TimeSpan.FromSeconds(5) },
                Policies = new PolicySet
                {
                    Inbound = [new PolicyReference { Type = "rateLimit", Config = JsonDocument.Parse("""{"limit":5}""").RootElement, Inheritance = PolicyInheritance.Override }],
                },
            };
            api.Operations.Add(new OperationEntity { Name = "get", Method = "GET", Path = "/{id}" });
            context.Apis.Add(api);
            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var loaded = await read.Apis.Include(a => a.Operations).SingleAsync(a => a.Id == apiId);

        loaded.Backend.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
        loaded.Backend.Destinations.ShouldBe(["http://a"]);
        var policy = loaded.Policies.Inbound.ShouldHaveSingleItem();
        policy.Inheritance.ShouldBe(PolicyInheritance.Override);
        policy.Config!.Value.GetProperty("limit").GetInt32().ShouldBe(5);
        loaded.Operations.ShouldHaveSingleItem().Path.ShouldBe("/{id}");
    }

    [Fact]
    public async Task ApiName_Duplicate_ViolatesUniqueIndex()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();
        context.Apis.Add(new ApiEntity { Name = "x", BasePath = "/x" });
        context.Apis.Add(new ApiEntity { Name = "x", BasePath = "/y" });

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task OperationName_DuplicateWithinApi_ViolatesUniqueIndex()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();
        var api = new ApiEntity { Name = "x", BasePath = "/x" };
        api.Operations.Add(new OperationEntity { Name = "op" });
        api.Operations.Add(new OperationEntity { Name = "op" });
        context.Apis.Add(api);

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DeleteApi_CascadesToOperations_AndDetachesProducts()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateContext())
        {
            var product = new ProductEntity { Name = "p" };
            var api = new ApiEntity { Name = "x", BasePath = "/x" };
            api.Operations.Add(new OperationEntity { Name = "op" });
            api.Products.Add(product);
            context.Apis.Add(api);
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            context.Apis.Remove(await context.Apis.SingleAsync());
            await context.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        (await verify.Operations.CountAsync()).ShouldBe(0);
        (await verify.Products.Include(p => p.Apis).SingleAsync()).Apis.ShouldBeEmpty();
    }

    [Fact]
    public async Task SubscriptionKeys_HashIsUniqueAcrossSubscriptions()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();
        var consumer = new ConsumerEntity { Name = "c" };
        var product = new ProductEntity { Name = "p" };
        context.AddRange(consumer, product);
        await context.SaveChangesAsync();
        foreach (var name in new[] { "s1", "s2" })
        {
            var subscription = new SubscriptionEntity { Name = name, ConsumerId = consumer.Id, ProductId = product.Id };
            subscription.AddKey("same-hash", "primary", DateTimeOffset.UtcNow);
            context.Subscriptions.Add(subscription);
        }

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Revisions_AreOrderedByTimestampAndNumber()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Revisions.AddRange(
            new Revision { Number = 2, CreatedAt = now.AddMinutes(2), ConfigJson = "{}", ConfigHash = "b" },
            new Revision { Number = 1, CreatedAt = now.AddMinutes(1), ConfigJson = "{}", ConfigHash = "a" },
            new Revision { Number = 3, CreatedAt = now.AddMinutes(3), ConfigJson = "{}", ConfigHash = "c" });
        await context.SaveChangesAsync();

        (await context.Revisions.OrderByDescending(r => r.CreatedAt).Select(r => r.Number).ToListAsync()).ShouldBe([3, 2, 1]);
        (await context.Revisions.Where(r => r.CreatedAt > now.AddMinutes(1.5)).CountAsync()).ShouldBe(2);
    }
}
