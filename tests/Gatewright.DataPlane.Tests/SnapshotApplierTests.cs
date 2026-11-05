// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Tests.Tls;
using Gatewright.DataPlane.Tls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Gatewright.DataPlane.Tests;

public sealed class SnapshotApplierTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance));
        services.AddGatewrightDataPlane();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider ServicesWithTls()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance));
        services.AddGatewrightDataPlane();
        services.AddGatewrightTls(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static ConfigSnapshot Snapshot(long version, string path = "/orders/{**rest}", string routeId = "orders", string clusterId = "orders")
    {
        var route = new RouteConfig { RouteId = routeId, ClusterId = clusterId, Match = new RouteMatch { Path = path } };
        var cluster = new ClusterConfig
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig> { ["d"] = new() { Address = "http://localhost:5000" } },
        };
        var pipeline = new RoutePipeline(routeId, new ApiInfo("orders", "/orders"), null, new PolicyPipeline(new Dictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>>()));
        return new ConfigSnapshot(
            version,
            "hash" + version,
            [route],
            [cluster],
            new Dictionary<string, RoutePipeline> { [routeId] = pipeline },
            SecurityData.Empty);
    }

    [Fact]
    public async Task Apply_ValidSnapshot_UpdatesYarpAndRegistry()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();

        var applied = await applier.ApplyAsync(Snapshot(1));

        applied.ShouldBeTrue();
        sp.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes.ShouldHaveSingleItem().RouteId.ShouldBe("orders");
        sp.GetRequiredService<PipelineRegistry>().Current!.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Apply_BeforeAnySnapshot_RegistryIsEmpty()
    {
        using var sp = Services();

        sp.GetRequiredService<PipelineRegistry>().HasSnapshot.ShouldBeFalse();
        sp.GetRequiredService<PipelineRegistry>().TryGetPipeline("orders", out _).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Apply_InvalidSnapshot_KeepsLastKnownGood()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        await applier.ApplyAsync(Snapshot(1));

        var applied = await applier.ApplyAsync(Snapshot(2, path: "no-leading-slash{"));

        applied.ShouldBeFalse();
        var config = sp.GetRequiredService<IProxyConfigProvider>().GetConfig();
        config.Routes.ShouldHaveSingleItem().Match.Path.ShouldBe("/orders/{**rest}");
        sp.GetRequiredService<PipelineRegistry>().Current!.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Apply_SnapshotReferencingMissingCluster_IsRejected()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        var route = new RouteConfig { RouteId = "r", ClusterId = "c", Match = new RouteMatch { Path = "/x" } };
        var snapshot = new ConfigSnapshot(1, "h", [route], [new ClusterConfig { ClusterId = "" }], new Dictionary<string, RoutePipeline>(), SecurityData.Empty);

        (await applier.ApplyAsync(snapshot)).ShouldBeFalse();
        sp.GetRequiredService<PipelineRegistry>().HasSnapshot.ShouldBeFalse();
    }

    [Fact]
    public async Task Registry_AfterSwap_StillFindsPipelinesOfPreviousSnapshot()
    {
        using var sp = Services();
        var applier = sp.GetRequiredService<SnapshotApplier>();
        await applier.ApplyAsync(Snapshot(1, routeId: "old", clusterId: "old"));
        await applier.ApplyAsync(Snapshot(2, routeId: "new", clusterId: "new"));
        var registry = sp.GetRequiredService<PipelineRegistry>();

        registry.TryGetPipeline("new", out _).ShouldBeTrue();
        registry.TryGetPipeline("old", out _).ShouldBeTrue();
        registry.TryGetPipeline("ghost", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ApplySecret_ForwardsToTheCertificateStore()
    {
        using var directory = new TempDirectory();
        var cert = TestCertificates.Write(directory.Path, "secret", "api.example.com");
        using var sp = ServicesWithTls();
        var store = sp.GetRequiredService<CertificateStore>();
        store.Apply(new SecurityData { Hostnames = [new HostnameDefinition { Name = "api.example.com", Certificate = new CertificateSource { Secret = "ns/tls" } }] });
        var applier = sp.GetRequiredService<SnapshotApplier>();

        applier.ApplySecret("ns/tls", await File.ReadAllTextAsync(cert.CertPath), await File.ReadAllTextAsync(cert.KeyPath));

        store.Select("api.example.com")!.Thumbprint.ShouldBe(cert.Thumbprint);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("gatewright-applier-certs-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
