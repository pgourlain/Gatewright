// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Gatewright.DataPlane.Tests;

public sealed class EndpointOverlayTests
{
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance));
        services.AddGatewrightDataPlane();
        return services.BuildServiceProvider();
    }

    private static ConfigSnapshot Snapshot(long version, BackendDefinition backend)
    {
        var registry = new PolicyRegistry([]);
        var translator = new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, new NoServices()));
        var result = translator.Translate(new GatewayConfig { Version = version, Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = backend }] });
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Snapshot!;
    }

    private static ClusterConfig OrdersCluster(ServiceProvider sp) =>
        sp.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters.Single(c => c.ClusterId == "orders");

    private static ServiceEndpointSet Endpoints(params string[] addresses) =>
        new(new Dictionary<string, IReadOnlyList<string>> { ["shop/orders:80"] = addresses });

    [Fact]
    public async Task Endpoints_ReceivedAfterTheSnapshot_BecomeTheClusterDestinations()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        await applier.ApplyAsync(Snapshot(1, new BackendDefinition { Service = "shop/orders:80" }));
        OrdersCluster(sp).Destinations!.ShouldBeEmpty();

        applier.ApplyEndpoints(Endpoints("http://10.0.0.1:8080", "http://10.0.0.2:8080"));

        OrdersCluster(sp).Destinations!.Values.Select(d => d.Address).ShouldBe(["http://10.0.0.1:8080", "http://10.0.0.2:8080"]);
    }

    [Fact]
    public async Task Endpoints_ReceivedBeforeTheSnapshot_AreUsedWhenItArrives()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        applier.ApplyEndpoints(Endpoints("http://10.0.0.1:8080"));

        await applier.ApplyAsync(Snapshot(1, new BackendDefinition { Service = "shop/orders:80" }));

        OrdersCluster(sp).Destinations!.Values.Select(d => d.Address).ShouldBe(["http://10.0.0.1:8080"]);
    }

    [Fact]
    public async Task ScalingDown_RemovesTheDestinationsWithoutANewConfigurationVersion()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        await applier.ApplyAsync(Snapshot(7, new BackendDefinition { Service = "shop/orders:80" }));
        applier.ApplyEndpoints(Endpoints("http://10.0.0.1:8080", "http://10.0.0.2:8080", "http://10.0.0.3:8080"));

        applier.ApplyEndpoints(Endpoints("http://10.0.0.2:8080"));

        OrdersCluster(sp).Destinations!.Values.Select(d => d.Address).ShouldBe(["http://10.0.0.2:8080"]);
        sp.GetRequiredService<PipelineRegistry>().Current!.Version.ShouldBe(7);
    }

    [Fact]
    public async Task StaticDestinations_AreNotTouchedByEndpoints()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        await applier.ApplyAsync(Snapshot(1, new BackendDefinition { Destinations = ["http://static:80"] }));

        applier.ApplyEndpoints(Endpoints("http://10.0.0.1:8080"));

        OrdersCluster(sp).Destinations!.Values.Select(d => d.Address).ShouldBe(["http://static:80"]);
    }

    [Fact]
    public async Task ServiceBackedClusters_GetPassiveHealthChecksByDefault()
    {
        using var sp = Services();
        await sp.GetRequiredService<SnapshotApplier>().ApplyAsync(Snapshot(1, new BackendDefinition { Service = "shop/orders:80" }));

        var health = OrdersCluster(sp).HealthCheck!;

        health.Passive!.Enabled.ShouldBe(true);
        health.Active.ShouldBeNull();
    }

    [Fact]
    public async Task ExplicitHealthChecks_AreTranslated()
    {
        using var sp = Services();
        var backend = new BackendDefinition
        {
            Destinations = ["http://a:80"],
            HealthCheck = new HealthCheckDefinition
            {
                Active = new ActiveHealthCheckDefinition { Interval = TimeSpan.FromSeconds(5), Timeout = TimeSpan.FromSeconds(2), Path = "/ready" },
                Passive = new PassiveHealthCheckDefinition { ReactivationPeriod = TimeSpan.FromSeconds(20) },
            },
        };

        await sp.GetRequiredService<SnapshotApplier>().ApplyAsync(Snapshot(1, backend));

        var health = OrdersCluster(sp).HealthCheck!;
        health.Active!.Path.ShouldBe("/ready");
        health.Active.Interval.ShouldBe(TimeSpan.FromSeconds(5));
        health.Passive!.ReactivationPeriod.ShouldBe(TimeSpan.FromSeconds(20));
    }
}
