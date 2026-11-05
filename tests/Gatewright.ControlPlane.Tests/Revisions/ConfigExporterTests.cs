// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;

namespace Gatewright.ControlPlane.Tests.Revisions;

public sealed class ConfigExporterTests
{
    [Fact]
    public async Task Export_FullDraft_MapsEntitiesToConfiguration()
    {
        using var h = new RevisionTestHarness();
        var product = new ProductEntity { Name = "gold", RequiresSubscription = true };
        var plan = new PlanEntity { Name = "premium" };
        var api = h.AddApi("orders");
        h.Db.Products.Add(product);
        h.Db.Plans.Add(new PlanEntity { Id = plan.Id, ProductId = product.Id, Name = plan.Name });
        h.Db.Operations.Add(new OperationEntity { ApiId = api.Id, Name = "get", Method = "GET", Path = "/{id}" });
        await h.Db.SaveChangesAsync();
        api.Products.Add(product);
        var consumer = new ConsumerEntity { Name = "acme" };
        h.Db.Consumers.Add(consumer);
        await h.Db.SaveChangesAsync();
        var subscription = new SubscriptionEntity { Name = "acme-gold", ConsumerId = consumer.Id, ProductId = product.Id, PlanId = plan.Id };
        subscription.AddKey("hash-a", "primary", DateTimeOffset.UtcNow);
        subscription.AddKey("hash-b", "secondary", DateTimeOffset.UtcNow);
        subscription.RevokeKey("secondary", DateTimeOffset.UtcNow);
        h.Db.Subscriptions.Add(subscription);
        await h.Db.SaveChangesAsync();

        var config = await new ConfigExporter(h.Db).ExportAsync(CancellationToken.None);

        var exportedApi = config.Apis.ShouldHaveSingleItem();
        exportedApi.Products.ShouldBe(["gold"]);
        exportedApi.Operations.ShouldHaveSingleItem().Path.ShouldBe("/{id}");
        config.Products.ShouldHaveSingleItem().RequiresSubscription.ShouldBeTrue();
        var exportedSubscription = config.Subscriptions.ShouldHaveSingleItem();
        exportedSubscription.Id.ShouldBe("acme-gold");
        exportedSubscription.Product.ShouldBe("gold");
        exportedSubscription.Plan.ShouldBe("premium");
        exportedSubscription.KeyHashes.ShouldBe(["hash-a"]);
    }

    [Fact]
    public async Task Export_IsDeterministic()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("b");
        h.AddApi("a");
        var exporter = new ConfigExporter(h.Db);

        var first = await exporter.ExportAsync(CancellationToken.None);
        var second = await exporter.ExportAsync(CancellationToken.None);

        first.Apis.Select(a => a.Name).ShouldBe(["a", "b"]);
        Gatewright.Core.Configuration.GatewayConfigSerializer.Serialize(first)
            .ShouldBe(Gatewright.Core.Configuration.GatewayConfigSerializer.Serialize(second));
    }
}
