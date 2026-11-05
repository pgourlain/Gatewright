// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Http;

namespace Gatewright.Abstractions.Tests;

public sealed class GatewayContextTests
{
    private static GatewayContext Create(OperationInfo? operation = null) =>
        new(new DefaultHttpContext(), "route-1", new ApiInfo("orders", "/orders", "v1"), operation);

    [Fact]
    public void Constructor_ValidArguments_ExposesRouting()
    {
        var operation = new OperationInfo("getOrder", "GET", "/{id}");

        var context = Create(operation);

        context.RouteId.ShouldBe("route-1");
        context.Api.Name.ShouldBe("orders");
        context.Operation.ShouldBeSameAs(operation);
        context.Identity.ShouldBeNull();
        context.Subscription.ShouldBeNull();
    }

    [Fact]
    public void Variables_SetAndRead_RoundTrips()
    {
        var context = Create();

        context.Variables["answer"] = 42;

        context.Variables["answer"].ShouldBe(42);
        context.Variables.ShouldBeSameAs(context.Variables);
    }

    [Fact]
    public void Constructor_BlankRouteId_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new GatewayContext(new DefaultHttpContext(), "", new ApiInfo("a", "/a")));
    }

    [Fact]
    public void Phase_Flags_CanBeCombined()
    {
        var both = PolicyPhase.Inbound | PolicyPhase.Outbound;

        both.HasFlag(PolicyPhase.Inbound).ShouldBeTrue();
        both.HasFlag(PolicyPhase.Backend).ShouldBeFalse();
    }

    [Fact]
    public void SubscriptionInfo_Defaults_AreActive()
    {
        var subscription = new SubscriptionInfo("sub-1", "product-1");

        subscription.IsActive.ShouldBeTrue();
        subscription.Plan.ShouldBeNull();
    }
}
