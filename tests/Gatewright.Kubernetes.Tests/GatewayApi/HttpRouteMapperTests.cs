// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Kubernetes.GatewayApi;
using k8s;
using k8s.Models;

namespace Gatewright.Kubernetes.Tests.GatewayApi;

public sealed class HttpRouteMapperTests
{
    private const string Prefix = "gatewright.io/";

    private static HttpRoute Route(string json) => KubernetesJson.Deserialize<HttpRoute>(json);

    private static HttpRoute Simple(string rules, string extraSpec = "", string annotations = "") => Route(
        """
        {"apiVersion":"gateway.networking.k8s.io/v1","kind":"HTTPRoute",
         "metadata":{"namespace":"shop","name":"orders","uid":"u-1","generation":3 ANNOTATIONS},
         "spec":{"parentRefs":[{"name":"gw"}] EXTRA, "rules":[RULES]}}
        """.Replace("ANNOTATIONS", annotations).Replace("EXTRA", extraSpec).Replace("RULES", rules.Replace("BACKEND", Backend)));

    private const string Backend = """{"name":"orders","port":8080}""";

    [Fact]
    public void Map_SinglePrefixWithoutMethod_IsACatchAllApiUnderItsBasePath()
    {
        var mapped = HttpRouteMapper.Map(Simple("""{"matches":[{"path":{"type":"PathPrefix","value":"/orders"}}],"backendRefs":[BACKEND]}""", annotations: ""","annotations":{"gatewright.io/product":"payments"}"""), Prefix);

        mapped.Problems.ShouldBeEmpty();
        mapped.Ref.ShouldBe("shop/orders");
        mapped.Uid.ShouldBe("u-1");
        mapped.Generation.ShouldBe(3);
        var api = mapped.Api!;
        api.Name.ShouldBe("shop.orders");
        api.BasePath.ShouldBe("/orders");
        api.Operations.ShouldBeEmpty();
        api.Backend.Service.ShouldBe("shop/orders:8080");
        api.Hosts.ShouldBeNull();
        api.Source!.Kind.ShouldBe("gatewayapi");
        api.Source.Ref.ShouldBe("shop/orders");
        api.Source.Product.ShouldBe("payments");
    }

    [Fact]
    public void Map_NoMatchAtAll_DefaultsToTheRootPrefix()
    {
        var api = HttpRouteMapper.Map(Simple("""{"backendRefs":[BACKEND]}"""), Prefix).Api!;

        api.BasePath.ShouldBe("/");
        api.Operations.ShouldBeEmpty();
        api.Source!.Product.ShouldBeNull();
    }

    [Fact]
    public void Map_Hostnames_BecomeHosts()
    {
        var api = HttpRouteMapper.Map(Simple("""{"backendRefs":[BACKEND]}""", extraSpec: ""","hostnames":["api.acme.com","*.acme.org"]"""), Prefix).Api!;

        api.Hosts.ShouldBe(["api.acme.com", "*.acme.org"]);
    }

    [Fact]
    public void Map_ExactAndPrefixMatches_BecomeOperationsRelativeToTheCommonBase()
    {
        var rules = """
            {"matches":[{"path":{"type":"Exact","value":"/orders/1"},"method":"get"},
                        {"path":{"type":"PathPrefix","value":"/orders/items"},"method":"POST"}],
             "backendRefs":[BACKEND]}
            """;

        var api = HttpRouteMapper.Map(Simple(rules), Prefix).Api!;

        api.BasePath.ShouldBe("/orders");
        api.Operations.Select(o => (o.Name, o.Method, o.Path)).ShouldBe(
        [
            ("r0m0", "GET", "/1"),
            ("r0m1", "POST", "/items/{**rest}"),
        ]);
    }

    [Fact]
    public void Map_MethodlessMatchBesideAnother_ExpandsToTheCommonMethods()
    {
        var rules = """
            {"matches":[{"path":{"value":"/a"}},{"path":{"value":"/b"},"method":"GET"}],"backendRefs":[BACKEND]}
            """;

        var api = HttpRouteMapper.Map(Simple(rules), Prefix).Api!;

        api.BasePath.ShouldBe("/");
        api.Operations.Count(o => o.Path == "/a/{**rest}").ShouldBe(7);
        api.Operations.Single(o => o.Path == "/b/{**rest}").Method.ShouldBe("GET");
    }

    [Fact]
    public void Map_DuplicateMatches_AreKeptOnce()
    {
        var rules = """
            {"matches":[{"path":{"type":"Exact","value":"/x/y"},"method":"GET"}],"backendRefs":[BACKEND]},
            {"matches":[{"path":{"type":"Exact","value":"/x/y"},"method":"GET"}],"backendRefs":[BACKEND]}
            """;

        HttpRouteMapper.Map(Simple(rules), Prefix).Api!.Operations.Count.ShouldBe(1);
    }

    public static TheoryData<string, string> Unsupported => new()
    {
        { """{"matches":[{"path":{"type":"RegularExpression","value":"/a.*"}}],"backendRefs":[{"name":"o","port":80}]}""", "RegularExpression" },
        { """{"matches":[{"headers":[{"name":"x","value":"y"}]}],"backendRefs":[{"name":"o","port":80}]}""", "header" },
        { """{"matches":[{"queryParams":[{"name":"x","value":"y"}]}],"backendRefs":[{"name":"o","port":80}]}""", "query" },
        { """{"filters":[{"type":"RequestHeaderModifier"}],"backendRefs":[{"name":"o","port":80}]}""", "filters" },
        { """{"timeouts":{"request":"1s"},"backendRefs":[{"name":"o","port":80}]}""", "timeouts" },
        { """{"matches":[{"method":"CONNECT"}],"backendRefs":[{"name":"o","port":80}]}""", "CONNECT" },
        { """{"backendRefs":[{"name":"o","port":80},{"name":"p","port":80}]}""", "Several distinct backends" },
        { """{"backendRefs":[{"name":"o","port":80,"namespace":"other"}]}""", "another namespace" },
        { """{"backendRefs":[{"name":"o","port":80,"kind":"Backend","group":"x.io"}]}""", "Service" },
        { """{"backendRefs":[{"name":"o"}]}""", "name and a port" },
        { """{"backendRefs":[{"name":"o","port":80,"weight":0}]}""", "weight" },
        { """{"matches":[{"path":{"value":"nope"}}],"backendRefs":[{"name":"o","port":80}]}""", "start with" },
        { """{"matches":[{"path":{"value":"/a"}}]}""", "no backend" },
    };

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void Map_UnsupportedFeature_IsReportedAndTheRouteIsNotApplied(string rule, string expected)
    {
        var mapped = HttpRouteMapper.Map(Simple(rule), Prefix);

        mapped.Api.ShouldBeNull();
        string.Join(" | ", mapped.Problems).ShouldContain(expected);
    }

    [Fact]
    public void Map_SameBackendInSeveralRules_IsNotSeveralBackends()
    {
        var rules = """
            {"matches":[{"path":{"value":"/a"}}],"backendRefs":[BACKEND]},{"matches":[{"path":{"value":"/b"}}],"backendRefs":[BACKEND]}
            """;

        HttpRouteMapper.Map(Simple(rules), Prefix).Api.ShouldNotBeNull();
    }

    [Fact]
    public void Map_RouteWithoutRules_IsReported()
    {
        var mapped = HttpRouteMapper.Map(Route("""{"metadata":{"namespace":"shop","name":"empty"},"spec":{"parentRefs":[{"name":"gw"}]}}"""), Prefix);

        mapped.Api.ShouldBeNull();
        mapped.Problems.ShouldContain("The route has no rule.");
    }

    [Fact]
    public void Map_ProducedApi_PassesTheConfigurationValidator()
    {
        var rules = """
            {"matches":[{"path":{"type":"Exact","value":"/orders/1"},"method":"GET"},{"path":{"value":"/orders"}}],"backendRefs":[BACKEND]}
            """;
        var api = HttpRouteMapper.Map(Simple(rules, extraSpec: ""","hostnames":["api.acme.com"]"""), Prefix).Api!;

        var errors = new Gatewright.Core.Validation.GatewayConfigValidator().Validate(new Gatewright.Core.Configuration.GatewayConfig { Apis = [api] });

        errors.ShouldBeEmpty(string.Join("; ", errors));
    }

    [Fact]
    public void Deserialize_RealisticRoute_ReadsTheFieldsWeNeed()
    {
        var route = Route("""
            {"apiVersion":"gateway.networking.k8s.io/v1","kind":"HTTPRoute","metadata":{"name":"r","namespace":"n","resourceVersion":"9"},
             "spec":{"parentRefs":[{"group":"gateway.networking.k8s.io","kind":"Gateway","name":"gw","namespace":"infra","sectionName":"https"}],
                     "rules":[{"matches":[{"path":{"type":"PathPrefix","value":"/"}}],"backendRefs":[{"name":"s","port":80,"weight":1}]}]},
             "status":{"parents":[]}}
            """);

        route.Spec!.ParentRefs!.Single().Namespace.ShouldBe("infra");
        route.Spec.Rules!.Single().BackendRefs!.Single().Weight.ShouldBe(1);
        JsonSerializer.Serialize(route).ShouldContain("\"spec\"");
    }
}
