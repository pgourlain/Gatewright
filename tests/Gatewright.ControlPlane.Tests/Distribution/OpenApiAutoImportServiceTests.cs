// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Distribution;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
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

public sealed class OpenApiAutoImportServiceTests : IDisposable
{
    private const string Document = """
        openapi: 3.0.3
        info: { title: Orders, version: "1" }
        paths:
          /orders:
            get: { operationId: list }
        """;

    private sealed class FakeStore(DiscoveryState state) : IDiscoveryStore
    {
        public DiscoveryState State { get; set; } = state;

        public Task SaveAsync(DiscoveryState value, CancellationToken cancellationToken)
        {
            State = value;
            return Task.CompletedTask;
        }

        public Task<DiscoveryState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
    }

    private sealed class FakeFetcher : IOpenApiFetcher
    {
        public string Document { get; set; } = "";

        public Exception? Failure { get; set; }

        public List<Uri> Requested { get; } = [];

        public Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            Requested.Add(uri);
            return Failure is null ? Task.FromResult(Document) : Task.FromException<string>(Failure);
        }
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _provider;
    private readonly FakeFetcher _fetcher = new() { Document = Document };

    public OpenApiAutoImportServiceTests()
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
        services.AddScoped<ApiImportService>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
    }

    private static DiscoveryState State(string? openApiPath = "/swagger/v1/swagger.json", string? basePath = null, string? product = null) =>
        new([new DiscoveredService("shop", "orders", [80], openApiPath, basePath, product)], ServiceEndpointSet.Empty);

    private OpenApiAutoImportService Service(IDiscoveryStore store, AutoImportOptions? options = null, ILeaderElection? leader = null) => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        store,
        _fetcher,
        Options.Create(options ?? new AutoImportOptions()),
        new FakeTimeProvider(),
        NullLogger<OpenApiAutoImportService>.Instance,
        leader);

    [Fact]
    public async Task AnnotatedService_IsImportedAsADraftApiBackedByTheService()
    {
        var service = Service(new FakeStore(State(basePath: "/api/orders")));

        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(1);

        await using var db = _database.CreateContext();
        var api = await db.Apis.Include(a => a.Operations).SingleAsync();
        api.Name.ShouldBe("orders");
        api.BasePath.ShouldBe("/api/orders");
        api.Backend.Service.ShouldBe("shop/orders:80");
        api.Operations.ShouldHaveSingleItem().Name.ShouldBe("list");
        _fetcher.Requested.ShouldHaveSingleItem().ToString().ShouldBe("http://orders.shop.svc/swagger/v1/swagger.json");
        var revision = await db.Revisions.SingleAsync();
        revision.CreatedBy.ShouldBe("system:auto-import");
        (await db.Environments.AsNoTracking().AllAsync(e => e.CurrentPublicationId == null)).ShouldBeTrue();
    }

    [Fact]
    public async Task UnchangedDocument_IsNotImportedAgain_ChangedOneUpdatesTheApi()
    {
        var service = Service(new FakeStore(State()));
        await service.RunOnceAsync(CancellationToken.None);

        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(0);
        _fetcher.Document = Document.Replace("operationId: list", "operationId: listAll", StringComparison.Ordinal);
        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(1);

        await using var db = _database.CreateContext();
        (await db.Revisions.CountAsync()).ShouldBe(2);
        (await db.Operations.SingleAsync()).Name.ShouldBe("listAll");
    }

    [Fact]
    public async Task ServiceWithoutOpenApiAnnotation_IsIgnored()
    {
        var service = Service(new FakeStore(State(openApiPath: null)));

        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(0);

        _fetcher.Requested.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchFailure_IsRecordedAndRetriedOnTheNextRun()
    {
        var service = Service(new FakeStore(State()));
        _fetcher.Failure = new HttpRequestException("connection refused");

        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(0);
        await using (var db = _database.CreateContext())
        {
            (await db.AutoImports.SingleAsync()).LastError!.ShouldContain("connection refused");
        }

        _fetcher.Failure = null;
        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(1);

        await using var verify = _database.CreateContext();
        (await verify.AutoImports.SingleAsync()).LastError.ShouldBeNull();
    }

    [Fact]
    public async Task InvalidDocument_IsRecordedAsAnErrorWithoutTouchingTheDraft()
    {
        _fetcher.Document = "{\"swagger\":\"2.0\"}";
        var service = Service(new FakeStore(State()));

        (await service.RunOnceAsync(CancellationToken.None)).ShouldBe(0);

        await using var db = _database.CreateContext();
        (await db.AutoImports.SingleAsync()).LastError!.ShouldContain("Unsupported OpenAPI version");
        (await db.Apis.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AutoPublish_PublishesTheImportToTheConfiguredEnvironments()
    {
        var service = Service(new FakeStore(State()), new AutoImportOptions { PublishTo = ["dev"] });

        await service.RunOnceAsync(CancellationToken.None);

        await using var db = _database.CreateContext();
        var dev = await db.Environments.SingleAsync(e => e.Name == "dev");
        dev.CurrentPublicationId.ShouldNotBeNull();
        (await db.Environments.SingleAsync(e => e.Name == "prod")).CurrentPublicationId.ShouldBeNull();
        (await db.Publications.SingleAsync()).PublishedBy.ShouldBe("system:auto-import");
    }

    [Fact]
    public async Task ServiceProductAnnotation_LinksTheApiToTheProduct()
    {
        await using (var db = _database.CreateContext())
        {
            db.Products.Add(new ProductEntity { Name = "starter" });
            await db.SaveChangesAsync();
        }

        await Service(new FakeStore(State(product: "starter"))).RunOnceAsync(CancellationToken.None);

        await using var verify = _database.CreateContext();
        (await verify.Apis.Include(a => a.Products).SingleAsync()).Products.ShouldHaveSingleItem().Name.ShouldBe("starter");
    }
}
