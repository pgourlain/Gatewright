// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Distribution;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.ControlPlane.Tests.Distribution;

public sealed class GatewayApiSynchronizerTests : IDisposable
{
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _provider;

    public GatewayApiSynchronizerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped(_ => _database.CreateContext());
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton(Options.Create(new AdminAuthOptions()));
        var registry = new PolicyRegistry([]);
        services.AddSingleton(new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, new NoServices())));
        services.AddScoped<ConfigExporter>();
        services.AddScoped<RevisionService>();
        services.AddScoped<ActorAccessor>();
        services.AddScoped<ChangeRecorder>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
    }

    private GatewayApiSynchronizer Synchronizer(GatewayApiSyncOptions? options = null) => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(options ?? new GatewayApiSyncOptions { Enabled = true }),
        NullLogger<GatewayApiSynchronizer>.Instance);

    private static GatewayApiRoute Route(string name, long generation = 1, string basePath = "/orders", string? product = "payments", string[]? hosts = null, string service = "shop/orders:80", params (string Name, string Method, string Path)[] operations) =>
        new(
            "shop/" + name,
            "uid-" + name,
            generation,
            new ApiDefinition
            {
                Name = "shop." + name,
                BasePath = basePath,
                Hosts = hosts,
                Backend = new BackendDefinition { Service = service },
                Operations = [.. operations.Select(o => new OperationDefinition { Name = o.Name, Method = o.Method, Path = o.Path })],
                Source = new ApiSource { Kind = ApiSource.GatewayApi, Ref = "shop/" + name, Product = product },
            },
            []);

    private static GatewayApiState State(params GatewayApiRoute[] routes) => new(routes);

    private async Task<List<ApiEntity>> ApisAsync()
    {
        await using var db = _database.CreateContext();
        return await db.Apis.Include(a => a.Operations).AsNoTracking().OrderBy(a => a.Name).ToListAsync();
    }

    private async Task<int> RevisionCountAsync()
    {
        await using var db = _database.CreateContext();
        return await db.Revisions.CountAsync();
    }

    [Fact]
    public async Task Apply_NewRoute_CreatesTheApiWithItsSourceInOneSystemRevision()
    {
        var results = await Synchronizer().ApplyAsync(State(Route("orders", hosts: ["api.acme.com"], operations: [("r0m0", "GET", "/{id}")])), CancellationToken.None);

        results.ShouldHaveSingleItem().Accepted.ShouldBeTrue();
        var api = (await ApisAsync()).ShouldHaveSingleItem();
        api.Name.ShouldBe("shop.orders");
        api.Source.ShouldBe("gatewayapi");
        api.SourceRef.ShouldBe("shop/orders");
        api.SourceUid.ShouldBe("uid-orders");
        api.SourceProduct.ShouldBe("payments");
        api.Hosts.ShouldBe(["api.acme.com"]);
        api.Backend.Service.ShouldBe("shop/orders:80");
        api.Operations.ShouldHaveSingleItem().Path.ShouldBe("/{id}");
        await using var db = _database.CreateContext();
        (await db.Revisions.SingleAsync()).CreatedBy.ShouldBe("system:gatewayapi");
        (await db.Environments.AsNoTracking().AllAsync(e => e.CurrentPublicationId == null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Apply_WithPublishTo_PublishesEvenThoughTheApiIsBlocked()
    {
        var options = new GatewayApiSyncOptions { Enabled = true, PublishTo = ["dev"] };

        await Synchronizer(options).ApplyAsync(State(Route("orders", product: null)), CancellationToken.None);

        await using var db = _database.CreateContext();
        (await db.Environments.SingleAsync(e => e.Name == "dev")).CurrentPublicationId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Apply_SameGeneration_ChangesNothingAndCreatesNoRevision()
    {
        var synchronizer = Synchronizer();
        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);
        var revisions = await RevisionCountAsync();

        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);

        (await RevisionCountAsync()).ShouldBe(revisions);
    }

    [Fact]
    public async Task Apply_ModifiedRoute_UpdatesTheApiAndItsOperations_KeepsItsPolicies()
    {
        var synchronizer = Synchronizer();
        await synchronizer.ApplyAsync(State(Route("orders", operations: [("r0m0", "GET", "/a"), ("r0m1", "GET", "/b")])), CancellationToken.None);
        await using (var db = _database.CreateContext())
        {
            var api = await db.Apis.SingleAsync();
            api.Policies = new PolicySet { Inbound = [new PolicyReference { Type = "jwt" }] };
            await db.SaveChangesAsync();
        }

        await synchronizer.ApplyAsync(State(Route("orders", generation: 2, basePath: "/v2/orders", service: "shop/orders:81", operations: [("r0m0", "POST", "/a")])), CancellationToken.None);

        var changed = (await ApisAsync()).ShouldHaveSingleItem();
        changed.BasePath.ShouldBe("/v2/orders");
        changed.Backend.Service.ShouldBe("shop/orders:81");
        var operation = changed.Operations.ShouldHaveSingleItem();
        (operation.Name, operation.Method, operation.Path).ShouldBe(("r0m0", "POST", "/a"));
        changed.Policies.Inbound.ShouldHaveSingleItem().Type.ShouldBe("jwt");
        changed.SourceGeneration.ShouldBe(2);
        (await RevisionCountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Apply_RouteMissingFromTheList_IsDeletedOnlyAfterTheGracePeriod()
    {
        var synchronizer = Synchronizer(new GatewayApiSyncOptions { Enabled = true, DeletionGraceReconciliations = 3 });
        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);

        await synchronizer.ApplyAsync(State(), CancellationToken.None);
        await synchronizer.ApplyAsync(State(), CancellationToken.None);
        (await ApisAsync()).Count.ShouldBe(1);
        await synchronizer.ApplyAsync(State(), CancellationToken.None);

        (await ApisAsync()).ShouldBeEmpty();
        await using var db = _database.CreateContext();
        (await db.Revisions.OrderBy(r => r.Number).LastAsync()).Summary.ShouldContain("-shop/orders");
    }

    [Fact]
    public async Task Apply_RouteThatComesBack_ResetsTheAbsenceCounter()
    {
        var synchronizer = Synchronizer(new GatewayApiSyncOptions { Enabled = true, DeletionGraceReconciliations = 2 });
        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);
        await synchronizer.ApplyAsync(State(), CancellationToken.None);
        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);

        await synchronizer.ApplyAsync(State(), CancellationToken.None);

        (await ApisAsync()).ShouldHaveSingleItem().SourceMissingCount.ShouldBe(1);
    }

    [Fact]
    public async Task Apply_TooManyDeletionsAtOnce_DeletesNothing()
    {
        var synchronizer = Synchronizer(new GatewayApiSyncOptions { Enabled = true, DeletionGraceReconciliations = 1, MaxDeletionsPerCycle = 2 });
        var routes = Enumerable.Range(0, 3).Select(i => Route("r" + i, basePath: "/r" + i)).ToArray();
        await synchronizer.ApplyAsync(State(routes), CancellationToken.None);

        await synchronizer.ApplyAsync(State(), CancellationToken.None);

        (await ApisAsync()).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Apply_ApiDeclaredByHand_IsNeverDeleted()
    {
        await using (var db = _database.CreateContext())
        {
            db.Apis.Add(new ApiEntity { Name = "manual", BasePath = "/manual", Backend = new BackendDefinition { Service = "shop/manual:80" } });
            await db.SaveChangesAsync();
        }

        var synchronizer = Synchronizer(new GatewayApiSyncOptions { Enabled = true, DeletionGraceReconciliations = 1 });
        for (var i = 0; i < 3; i++)
        {
            await synchronizer.ApplyAsync(State(), CancellationToken.None);
        }

        (await ApisAsync()).ShouldHaveSingleItem().Name.ShouldBe("manual");
    }

    [Theory]
    [InlineData("/manual", null, "other")]
    [InlineData("/elsewhere", null, "shop.orders")]
    public async Task Apply_RouteConflictingWithAnApiDeclaredByHand_IsRefusedAndTheManualApiIsKept(string basePath, string? host, string manualName)
    {
        await using (var db = _database.CreateContext())
        {
            db.Apis.Add(new ApiEntity { Name = manualName, BasePath = "/manual", Backend = new BackendDefinition { Service = "shop/manual:80" } });
            await db.SaveChangesAsync();
        }

        var route = Route("orders", basePath: basePath == "/manual" ? "/manual/" : basePath, hosts: host is null ? null : [host]);
        var results = await Synchronizer().ApplyAsync(State(route), CancellationToken.None);

        var result = results.ShouldHaveSingleItem();
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("Conflict");
        (await ApisAsync()).ShouldHaveSingleItem().Name.ShouldBe(manualName);
    }

    [Fact]
    public async Task Apply_SameBasePathOnDifferentHosts_IsNotAConflict()
    {
        var results = await Synchronizer().ApplyAsync(
            State(Route("a", hosts: ["a.acme.com"]), Route("b", hosts: ["b.acme.com"])),
            CancellationToken.None);

        results.ShouldAllBe(r => r.Accepted);
        (await ApisAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Apply_TwoNewRoutesWithTheSameBasePath_OnlyTheFirstIsApplied()
    {
        var results = await Synchronizer().ApplyAsync(State(Route("a"), Route("b")), CancellationToken.None);

        results.Select(r => r.Accepted).ShouldBe([true, false]);
    }

    [Fact]
    public async Task Apply_RouteThatBecomesUnsupported_KeepsTheLastGoodApiAndReportsTheProblem()
    {
        var synchronizer = Synchronizer();
        await synchronizer.ApplyAsync(State(Route("orders")), CancellationToken.None);
        var broken = new GatewayApiRoute("shop/orders", "uid-orders", 2, null, ["rules[0]: filters are not supported."]);

        var results = await synchronizer.ApplyAsync(State(broken), CancellationToken.None);

        var result = results.ShouldHaveSingleItem();
        (result.Accepted, result.Reason).ShouldBe((false, "UnsupportedValue"));
        result.Message.ShouldContain("filters");
        (await ApisAsync()).ShouldHaveSingleItem().SourceGeneration.ShouldBe(1);
    }

    [Fact]
    public async Task Apply_WithWriteStatus_ReportsWhyAnAcceptedApiIsBlocked()
    {
        await using (var db = _database.CreateContext())
        {
            db.Products.Add(new ProductEntity { Name = "empty" });
            await db.SaveChangesAsync();
        }

        var options = new GatewayApiSyncOptions { Enabled = true, WriteStatus = true };
        var results = await Synchronizer(options).ApplyAsync(
            State(Route("none", basePath: "/none", product: null), Route("ghost", basePath: "/ghost", product: "nope"), Route("bare", basePath: "/bare", product: "empty")),
            CancellationToken.None);

        results.Select(r => (r.Ref, r.Accepted, r.BlockedReason)).ShouldBe(
        [
            ("shop/none", true, "MissingProduct"),
            ("shop/ghost", true, "ProductNotFound"),
            ("shop/bare", true, "NoPolicy"),
        ]);
    }

    [Fact]
    public async Task Apply_WithoutWriteStatus_DoesNotComputeBlockedReasons()
    {
        var results = await Synchronizer().ApplyAsync(State(Route("none", product: null)), CancellationToken.None);

        results.ShouldHaveSingleItem().BlockedReason.ShouldBeNull();
    }
}
