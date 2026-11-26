// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Kubernetes.GatewayApi;
using k8s;
using k8s.Models;

namespace Gatewright.Kubernetes.Tests.GatewayApi;

public sealed class GatewayApiAggregatorTests
{
    private static Gateway Gw(string ns, string name, string className) => new()
    {
        Metadata = new V1ObjectMeta { NamespaceProperty = ns, Name = name },
        Spec = new GatewaySpec { GatewayClassName = className },
    };

    private static HttpRoute Route(string ns, string name, string parentName, string? parentNs = null, string? kind = null) => KubernetesJson.Deserialize<HttpRoute>(
        """
        {"metadata":{"namespace":"@NS@","name":"@NAME@"},
         "spec":{"parentRefs":[{"name":"@PARENT@" @PARENTNS@ @KIND@}],
                 "rules":[{"backendRefs":[{"name":"svc","port":80}]}]}}
        """.Replace("@NS@", ns).Replace("@NAME@", name).Replace("@PARENTNS@", parentNs is null ? "" : $",\"namespace\":\"{parentNs}\"")
            .Replace("@PARENT@", parentName).Replace("@KIND@", kind is null ? "" : $",\"kind\":\"{kind}\""));

    private static GatewayApiAggregator Create() => new("gatewright.io/", "gatewright");

    [Fact]
    public void Snapshot_RouteOfAGatewrightGateway_IsMapped()
    {
        var aggregator = Create();
        aggregator.UpsertGateway(Gw("infra", "edge", "gatewright"));
        aggregator.UpsertRoute(Route("shop", "orders", "edge", parentNs: "infra"));

        aggregator.Snapshot().Select(r => r.Ref).ShouldBe(["shop/orders"]);
    }

    [Fact]
    public void Snapshot_RouteOfAnotherController_IsIgnored()
    {
        var aggregator = Create();
        aggregator.UpsertGateway(Gw("infra", "edge", "gatewright"));
        aggregator.UpsertGateway(Gw("infra", "other", "nginx"));
        aggregator.UpsertRoute(Route("shop", "theirs", "other", parentNs: "infra"));
        aggregator.UpsertRoute(Route("shop", "unknown", "missing", parentNs: "infra"));
        aggregator.UpsertRoute(Route("shop", "wrongkind", "edge", parentNs: "infra", kind: "Service"));

        aggregator.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Snapshot_ParentWithoutNamespace_MeansTheNamespaceOfTheRoute()
    {
        var aggregator = Create();
        aggregator.UpsertGateway(Gw("shop", "edge", "gatewright"));
        aggregator.UpsertRoute(Route("shop", "orders", "edge"));
        aggregator.UpsertRoute(Route("other", "orders", "edge"));

        aggregator.Snapshot().Select(r => r.Ref).ShouldBe(["shop/orders"]);
    }

    [Fact]
    public void Snapshot_RemovedRouteAndRemovedGateway_Disappear()
    {
        var aggregator = Create();
        var gateway = Gw("infra", "edge", "gatewright");
        var route = Route("shop", "orders", "edge", parentNs: "infra");
        aggregator.UpsertGateway(gateway);
        aggregator.UpsertRoute(route);

        aggregator.RemoveRoute(route);
        aggregator.Snapshot().ShouldBeEmpty();

        aggregator.UpsertRoute(route);
        aggregator.RemoveGateway(gateway);
        aggregator.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void IsReady_OnlyWhenBothListsAreCompleteAndNotBeingRebuilt()
    {
        var aggregator = Create();
        aggregator.IsReady.ShouldBeFalse();

        aggregator.MarkRoutesListed();
        aggregator.IsReady.ShouldBeFalse();
        aggregator.MarkGatewaysListed();
        aggregator.IsReady.ShouldBeTrue();

        aggregator.ClearRoutes();
        aggregator.IsReady.ShouldBeFalse();
        aggregator.MarkRoutesListed();
        aggregator.ClearGateways();
        aggregator.IsReady.ShouldBeFalse();
        aggregator.MarkGatewaysListed();
        aggregator.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void ReferencedServices_ListsTheBackendsOfApplicableRoutesOnly()
    {
        var aggregator = Create();
        aggregator.UpsertGateway(Gw("infra", "edge", "gatewright"));
        aggregator.UpsertRoute(Route("shop", "orders", "edge", parentNs: "infra"));
        aggregator.UpsertRoute(KubernetesJson.Deserialize<HttpRoute>("""
            {"metadata":{"namespace":"shop","name":"bad"},"spec":{"parentRefs":[{"name":"edge","namespace":"infra"}],"rules":[{"filters":[{"type":"X"}],"backendRefs":[{"name":"nope","port":80}]}]}}
            """));

        GatewayApiAggregator.ReferencedServices(aggregator.Snapshot()).ShouldBe(["shop/svc"]);
    }

    [Fact]
    public void References_SetWithTheSameServices_DoesNotRaiseChanged()
    {
        var references = new GatewayApiReferences();
        var raised = 0;
        references.Changed += () => raised++;

        references.Set(["shop/a"]);
        references.Set(["shop/a"]);
        references.Set(["shop/a", "shop/b"]);

        raised.ShouldBe(2);
        references.Contains("shop", "b").ShouldBeTrue();
        references.Contains("shop", "c").ShouldBeFalse();
    }

    [Fact]
    public void ClusterStateAggregator_ReferencedServiceWithoutAnnotation_GetsItsEndpointsButIsNotListed()
    {
        var references = new GatewayApiReferences();
        references.Set(["shop/orders"]);
        var aggregator = new ClusterStateAggregator("gatewright.io/", references);
        aggregator.UpsertService(new V1Service
        {
            Metadata = new V1ObjectMeta { NamespaceProperty = "shop", Name = "orders" },
            Spec = new V1ServiceSpec { Ports = [new V1ServicePort { Port = 8080, TargetPort = 80 }] },
        });
        aggregator.UpsertSlice(new V1EndpointSlice
        {
            Metadata = new V1ObjectMeta
            {
                NamespaceProperty = "shop",
                Name = "orders-1",
                Labels = new Dictionary<string, string> { [KubernetesMapper.ServiceNameLabel] = "orders" },
            },
            AddressType = "IPv4",
            Ports = [new Discoveryv1EndpointPort { Port = 80 }],
            Endpoints = [new V1Endpoint { Addresses = ["10.0.0.7"], Conditions = new V1EndpointConditions { Ready = true } }],
        });

        var state = aggregator.Snapshot();

        state.Services.ShouldBeEmpty();
        state.Endpoints.For("shop/orders:8080").Count.ShouldBe(1);
    }
}
