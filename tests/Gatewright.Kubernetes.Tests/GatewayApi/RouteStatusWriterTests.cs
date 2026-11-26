// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Gatewright.Core.Discovery;
using Gatewright.Kubernetes.GatewayApi;

namespace Gatewright.Kubernetes.Tests.GatewayApi;

public sealed class RouteStatusWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<string> Served = new HashSet<string> { "infra/edge" };

    private static JsonNode Route(string? status = null) => JsonNode.Parse($$"""
        {"metadata":{"namespace":"shop","name":"orders","generation":4},
         "spec":{"parentRefs":[{"name":"edge","namespace":"infra"},{"name":"other","namespace":"infra"}]}
         {{(status is null ? "" : ",\"status\":" + status)}}}
        """)!;

    private static GatewayApiRouteResult Accepted(string? blocked = null) => new("shop/orders", true, "Accepted", "The route is applied.", blocked);

    private static string Condition(JsonArray parents, int index, string type, string field) =>
        parents[index]!["conditions"]!.AsArray().Single(c => c!["type"]!.GetValue<string>() == type)![field]!.ToString();

    [Fact]
    public void BuildParents_FirstWrite_AddsOneEntryPerServedParentOnly()
    {
        var parents = RouteStatusWriter.BuildParents(Route(), Served, Accepted(), Now)!;

        parents.Count.ShouldBe(1);
        parents[0]!["controllerName"]!.GetValue<string>().ShouldBe("gatewright.io/gateway-controller");
        parents[0]!["parentRef"]!["name"]!.GetValue<string>().ShouldBe("edge");
        Condition(parents, 0, "Accepted", "status").ShouldBe("True");
        Condition(parents, 0, "Accepted", "observedGeneration").ShouldBe("4");
        Condition(parents, 0, "Accepted", "lastTransitionTime").ShouldBe("2026-11-26T12:00:00Z");
        Condition(parents, 0, RouteStatusWriter.ConfiguredCondition, "status").ShouldBe("True");
    }

    [Fact]
    public void BuildParents_BlockedApi_StaysAcceptedButIsNotConfigured()
    {
        var parents = RouteStatusWriter.BuildParents(Route(), Served, Accepted("MissingProduct"), Now)!;

        Condition(parents, 0, "Accepted", "status").ShouldBe("True");
        Condition(parents, 0, RouteStatusWriter.ConfiguredCondition, "status").ShouldBe("False");
        Condition(parents, 0, RouteStatusWriter.ConfiguredCondition, "reason").ShouldBe("MissingProduct");
        Condition(parents, 0, RouteStatusWriter.ConfiguredCondition, "message").ShouldContain("gatewright.io/product");
    }

    [Fact]
    public void BuildParents_UnsupportedRoute_IsNotAccepted()
    {
        var result = new GatewayApiRouteResult("shop/orders", false, "UnsupportedValue", "filters are not supported.");

        var parents = RouteStatusWriter.BuildParents(Route(), Served, result, Now)!;

        Condition(parents, 0, "Accepted", "status").ShouldBe("False");
        Condition(parents, 0, "Accepted", "reason").ShouldBe("UnsupportedValue");
        Condition(parents, 0, "Accepted", "message").ShouldContain("filters");
    }

    [Fact]
    public void BuildParents_NothingChanged_ReturnsNull()
    {
        var first = RouteStatusWriter.BuildParents(Route(), Served, Accepted(), Now)!;
        var written = Route($$"""{"parents":{{first.ToJsonString()}}}""");

        RouteStatusWriter.BuildParents(written, Served, Accepted(), Now.AddHours(1)).ShouldBeNull();
    }

    [Fact]
    public void BuildParents_StatusFlips_MovesTheTransitionTimeOnlyForTheConditionThatChanged()
    {
        var first = RouteStatusWriter.BuildParents(Route(), Served, Accepted(), Now)!;
        var written = Route($$"""{"parents":{{first.ToJsonString()}}}""");

        var next = RouteStatusWriter.BuildParents(written, Served, Accepted("NoPolicy"), Now.AddHours(1))!;

        Condition(next, 0, "Accepted", "lastTransitionTime").ShouldBe("2026-11-26T12:00:00Z");
        Condition(next, 0, RouteStatusWriter.ConfiguredCondition, "lastTransitionTime").ShouldBe("2026-11-26T13:00:00Z");
    }

    [Fact]
    public void BuildParents_EntriesOfOtherControllers_AreKept()
    {
        var other = """{"parents":[{"parentRef":{"name":"other","namespace":"infra"},"controllerName":"example.com/gw","conditions":[{"type":"Accepted","status":"True"}]}]}""";

        var parents = RouteStatusWriter.BuildParents(Route(other), Served, Accepted(), Now)!;

        parents.Count.ShouldBe(2);
        parents[0]!["controllerName"]!.GetValue<string>().ShouldBe("example.com/gw");
        parents[1]!["controllerName"]!.GetValue<string>().ShouldBe("gatewright.io/gateway-controller");
    }

    [Fact]
    public void BuildParents_RouteNoLongerAttached_DropsOurStaleEntry()
    {
        var first = RouteStatusWriter.BuildParents(Route(), Served, Accepted(), Now)!;
        var written = Route($$"""{"parents":{{first.ToJsonString()}}}""");

        var next = RouteStatusWriter.BuildParents(written, new HashSet<string>(), Accepted(), Now);

        next.ShouldNotBeNull();
        next.Count.ShouldBe(0);
    }
}
