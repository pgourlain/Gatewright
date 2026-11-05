// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using k8s.Models;

namespace Gatewright.Kubernetes.Tests;

public sealed class KubernetesMapperTests
{
    private const string Prefix = "gatewright.io/";

    private static V1Service Service(string ns, string name, Dictionary<string, string>? annotations = null, params (string? Name, int Port, string? AppProtocol)[] ports) => new()
    {
        Metadata = new V1ObjectMeta { NamespaceProperty = ns, Name = name, Annotations = annotations },
        Spec = new V1ServiceSpec
        {
            Ports = [.. ports.Select(p => new V1ServicePort { Name = p.Name, Port = p.Port, AppProtocol = p.AppProtocol })],
        },
    };

    private static V1EndpointSlice Slice(string ns, string service, string? portName, int targetPort, params (string Address, bool? Ready, bool? Terminating)[] endpoints) => new()
    {
        Metadata = new V1ObjectMeta
        {
            NamespaceProperty = ns,
            Name = service + "-" + Guid.NewGuid().ToString("N")[..5],
            Labels = new Dictionary<string, string> { [KubernetesMapper.ServiceNameLabel] = service },
        },
        AddressType = "IPv4",
        Ports = [new Discoveryv1EndpointPort { Name = portName, Port = targetPort }],
        Endpoints = [.. endpoints.Select(e => new V1Endpoint
        {
            Addresses = [e.Address],
            Conditions = new V1EndpointConditions { Ready = e.Ready, Terminating = e.Terminating },
        })],
    };

    private static Dictionary<string, string> Exposed(params (string Key, string Value)[] extra) =>
        new[] { (Prefix + "expose", "true") }.Concat(extra).ToDictionary(p => p.Item1, p => p.Item2);

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void IsExposed_OnlyForTheTrueAnnotation(string value, bool expected)
    {
        var service = Service("shop", "orders", new Dictionary<string, string> { [Prefix + "expose"] = value }, (null, 80, null));

        KubernetesMapper.IsExposed(service, Prefix).ShouldBe(expected);
    }

    [Fact]
    public void IsExposed_WithoutAnnotations_IsFalse()
    {
        KubernetesMapper.IsExposed(Service("shop", "orders", null, (null, 80, null)), Prefix).ShouldBeFalse();
    }

    [Fact]
    public void ToDiscovered_ReadsPortsAndAnnotations()
    {
        var service = Service(
            "shop",
            "orders",
            Exposed((Prefix + "openapi-path", "/swagger/v1/swagger.json"), (Prefix + "base-path", "/orders"), (Prefix + "product", "starter")),
            ("http", 8080, null),
            ("metrics", 9090, null));

        var discovered = KubernetesMapper.ToDiscovered(service, Prefix);

        discovered.Ports.ShouldBe([8080, 9090]);
        discovered.OpenApiPath.ShouldBe("/swagger/v1/swagger.json");
        discovered.BasePath.ShouldBe("/orders");
        discovered.Product.ShouldBe("starter");
        discovered.Reference(8080).ShouldBe("shop/orders:8080");
    }

    [Fact]
    public void ToEndpoints_OnlyReadyEndpointsAreIncluded_TerminatingPodsAreDropped()
    {
        var service = Service("shop", "orders", Exposed(), (null, 80, null));
        var slice = Slice("shop", "orders", null, 8080, ("10.0.0.1", true, false), ("10.0.0.2", false, false), ("10.0.0.3", true, true), ("10.0.0.4", null, null));

        var endpoints = KubernetesMapper.ToEndpoints(service, [slice]);

        endpoints["shop/orders:80"].ShouldBe(["http://10.0.0.1:8080", "http://10.0.0.4:8080"]);
    }

    [Fact]
    public void ToEndpoints_MapsEachServicePortToItsTargetPort_AndHttpsByName()
    {
        var service = Service("shop", "orders", Exposed(), ("http", 80, null), ("https", 443, null));
        var http = Slice("shop", "orders", "http", 8080, ("10.0.0.1", true, false));
        var https = Slice("shop", "orders", "https", 8443, ("10.0.0.1", true, false));
        var combined = new V1EndpointSlice
        {
            Metadata = http.Metadata,
            AddressType = "IPv4",
            Ports = [.. http.Ports, .. https.Ports],
            Endpoints = http.Endpoints,
        };

        var endpoints = KubernetesMapper.ToEndpoints(service, [combined]);

        endpoints["shop/orders:80"].ShouldBe(["http://10.0.0.1:8080"]);
        endpoints["shop/orders:443"].ShouldBe(["https://10.0.0.1:8443"]);
    }

    [Fact]
    public void ToEndpoints_SeveralSlices_AreMergedWithoutDuplicates()
    {
        var service = Service("shop", "orders", Exposed(), (null, 80, null));
        var first = Slice("shop", "orders", null, 8080, ("10.0.0.1", true, false), ("10.0.0.2", true, false));
        var second = Slice("shop", "orders", null, 8080, ("10.0.0.2", true, false), ("10.0.0.3", true, false));

        KubernetesMapper.ToEndpoints(service, [first, second])["shop/orders:80"].ShouldBe(["http://10.0.0.1:8080", "http://10.0.0.2:8080", "http://10.0.0.3:8080"]);
    }

    [Fact]
    public void ToEndpoints_Ipv6AddressesAreBracketed()
    {
        var service = Service("shop", "orders", Exposed(), (null, 80, null));
        var slice = Slice("shop", "orders", null, 8080, ("fd00::5", true, false));
        slice.AddressType = "IPv6";

        KubernetesMapper.ToEndpoints(service, [slice])["shop/orders:80"].ShouldBe(["http://[fd00::5]:8080"]);
    }

    [Fact]
    public void ToEndpoints_NoSlices_YieldsAnEmptyList()
    {
        var service = Service("shop", "orders", Exposed(), (null, 80, null));

        KubernetesMapper.ToEndpoints(service, [])["shop/orders:80"].ShouldBeEmpty();
    }

    [Fact]
    public void Aggregator_CombinesExposedServicesWithTheirSlicesAndIgnoresOthers()
    {
        var aggregator = new ClusterStateAggregator(Prefix);
        aggregator.UpsertService(Service("shop", "orders", Exposed(), (null, 80, null)));
        aggregator.UpsertService(Service("shop", "internal", null, (null, 80, null)));
        aggregator.UpsertSlice(Slice("shop", "orders", null, 8080, ("10.0.0.1", true, false)));
        aggregator.UpsertSlice(Slice("shop", "internal", null, 8080, ("10.0.0.9", true, false)));
        aggregator.UpsertSlice(Slice("other", "orders", null, 8080, ("10.9.9.9", true, false)));

        var state = aggregator.Snapshot();

        state.Services.ShouldHaveSingleItem().Name.ShouldBe("orders");
        state.Endpoints.Addresses.Keys.ShouldBe(["shop/orders:80"]);
        state.Endpoints.For("shop/orders:80").ShouldBe(["http://10.0.0.1:8080"]);
    }

    [Fact]
    public void Aggregator_RemovalsAndClears_UpdateTheState()
    {
        var aggregator = new ClusterStateAggregator(Prefix);
        var service = Service("shop", "orders", Exposed(), (null, 80, null));
        var slice = Slice("shop", "orders", null, 8080, ("10.0.0.1", true, false));
        aggregator.UpsertService(service);
        aggregator.UpsertSlice(slice);

        aggregator.RemoveSlice(slice);
        aggregator.Snapshot().Endpoints.For("shop/orders:80").ShouldBeEmpty();
        aggregator.UpsertSlice(slice);
        aggregator.Snapshot().Endpoints.For("shop/orders:80").Count.ShouldBe(1);
        aggregator.RemoveService(service);
        aggregator.Snapshot().Services.ShouldBeEmpty();
        aggregator.ClearSlices();
        aggregator.ClearServices();
        aggregator.Snapshot().Endpoints.Addresses.ShouldBeEmpty();
    }
}
