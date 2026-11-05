// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;

namespace Gatewright.Core.Tests.Snapshots;

public sealed class SnapshotTranslatorTests
{
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static SnapshotTranslator Translator()
    {
        var registry = new PolicyRegistry([]);
        return new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, new NoServices()));
    }

    private static GatewayConfig Sample(long version = 1) => new()
    {
        Version = version,
        Apis =
        [
            new ApiDefinition
            {
                Name = "orders",
                BasePath = "/orders/",
                Version = "v1",
                Backend = new BackendDefinition
                {
                    Destinations = ["http://a:80", "http://b:80"],
                    Timeout = TimeSpan.FromSeconds(10),
                    HttpVersion = "2",
                },
                Operations =
                [
                    new OperationDefinition { Name = "list", Method = "get", Path = "/" },
                    new OperationDefinition { Name = "get", Method = "GET", Path = "/{id}" },
                ],
            },
            new ApiDefinition
            {
                Name = "legacy",
                BasePath = "/legacy",
                Backend = new BackendDefinition { Service = "shop/legacy:80" },
            },
        ],
    };

    [Fact]
    public void Translate_Sample_ProducesRoutePerOperationAndPerOperationlessApi()
    {
        var result = Translator().Translate(Sample());

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        var snapshot = result.Snapshot!;
        snapshot.Routes.Select(r => r.RouteId).ShouldBe(["orders:list", "orders:get", "legacy"]);
        snapshot.Routes[0].Match.Path.ShouldBe("/orders");
        snapshot.Routes[0].Match.Methods.ShouldBe(["GET"]);
        snapshot.Routes[1].Match.Path.ShouldBe("/orders/{id}");
        snapshot.Routes[2].Match.Path.ShouldBe("/legacy/{**remainder}");
        snapshot.Routes[2].Match.Methods.ShouldBeNull();
        snapshot.Pipelines.Keys.ShouldBe(["orders:list", "orders:get", "legacy"], ignoreOrder: true);
    }

    [Fact]
    public void Translate_Sample_SetsRouteMetadataAndPrefixTransform()
    {
        var route = Translator().Translate(Sample()).Snapshot!.Routes[1];

        route.ClusterId.ShouldBe("orders");
        route.Metadata![SnapshotTranslator.ApiMetadataKey].ShouldBe("orders");
        route.Metadata[SnapshotTranslator.OperationMetadataKey].ShouldBe("get");
        route.Transforms!.ShouldHaveSingleItem()["PathRemovePrefix"].ShouldBe("/orders");
    }

    [Fact]
    public void Translate_Sample_BuildsClustersWithDestinationsAndTimeout()
    {
        var clusters = Translator().Translate(Sample()).Snapshot!.Clusters;

        var orders = clusters.Single(c => c.ClusterId == "orders");
        orders.Destinations!.Select(d => d.Value.Address).ShouldBe(["http://a:80", "http://b:80"]);
        orders.HttpRequest!.ActivityTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        orders.HttpRequest.Version.ShouldBe(new Version(2, 0));
        var legacy = clusters.Single(c => c.ClusterId == "legacy");
        legacy.Destinations.ShouldBeEmpty();
        legacy.Metadata![SnapshotTranslator.ServiceMetadataKey].ShouldBe("shop/legacy:80");
    }

    [Fact]
    public void Translate_Sample_PipelinesCarryApiAndOperation()
    {
        var pipeline = Translator().Translate(Sample()).Snapshot!.Pipelines["orders:get"];

        pipeline.Api.Name.ShouldBe("orders");
        pipeline.Operation!.Method.ShouldBe("GET");
        pipeline.Operation.PathTemplate.ShouldBe("/{id}");
    }

    [Fact]
    public void Translate_SameConfigTwice_ProducesSameHash()
    {
        var a = Translator().Translate(Sample()).Snapshot!;
        var b = Translator().Translate(GatewayConfigSerializer.Deserialize(GatewayConfigSerializer.Serialize(Sample()))).Snapshot!;

        a.Hash.ShouldBe(b.Hash);
        a.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Translate_VersionChangeOnly_KeepsHash()
    {
        var a = Translator().Translate(Sample(1)).Snapshot!;
        var b = Translator().Translate(Sample(2)).Snapshot!;

        a.Hash.ShouldBe(b.Hash);
        b.Version.ShouldBe(2);
    }

    [Fact]
    public void Translate_ContentChange_ChangesHash()
    {
        var changed = Sample() with { Products = [new ProductDefinition { Name = "p" }] };

        Translator().Translate(changed).Snapshot!.Hash.ShouldNotBe(Translator().Translate(Sample()).Snapshot!.Hash);
    }

    [Fact]
    public void Translate_SharedPolicyOrderDoesNotMatter_ForHash()
    {
        PolicyReference P(string t) => new() { Type = t };
        var a = new GatewayConfig { Policies = new Dictionary<string, PolicyReference> { ["x"] = P("a"), ["y"] = P("b") } };
        var b = new GatewayConfig { Policies = new Dictionary<string, PolicyReference> { ["y"] = P("b"), ["x"] = P("a") } };

        ConfigHasher.Hash(a).ShouldBe(ConfigHasher.Hash(b));
    }

    [Fact]
    public void Translate_InvalidConfig_ReturnsErrorsAndNoSnapshot()
    {
        var invalid = new GatewayConfig
        {
            Apis = [new ApiDefinition { Name = "a", BasePath = "bad", Backend = new BackendDefinition() }],
        };

        var result = Translator().Translate(invalid);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("api.base-path-invalid"));
    }

    [Fact]
    public void Translate_ExplicitHttpVersion_UsesExactVersionPolicyForGrpcAndH2c()
    {
        var config = new GatewayConfig
        {
            Apis = [new ApiDefinition { Name = "grpc", BasePath = "/g", Backend = new BackendDefinition { Destinations = ["http://b:80"], HttpVersion = "2" } }],
        };

        var cluster = Translator().Translate(config).Snapshot!.Clusters.Single();

        cluster.HttpRequest!.Version.ShouldBe(new Version(2, 0));
        cluster.HttpRequest.VersionPolicy.ShouldBe(System.Net.Http.HttpVersionPolicy.RequestVersionExact);
    }

    [Fact]
    public void Translate_Hostnames_ReachTheSecurityData()
    {
        var config = new GatewayConfig
        {
            Hostnames = [new HostnameDefinition { Name = "api.example.com", RedirectHttpToHttps = true }],
            Apis = [new ApiDefinition { Name = "a", BasePath = "/a", Backend = new BackendDefinition { Destinations = ["http://b:80"] } }],
        };

        Translator().Translate(config).Snapshot!.Security.Hostnames.ShouldHaveSingleItem().Name.ShouldBe("api.example.com");
    }
}
