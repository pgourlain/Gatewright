// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;

namespace Gatewright.Policies.Tests;

public sealed class VariableTemplateTests
{
    private static GatewayContext Context()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/orders/1";
        http.Request.Headers["x-in"] = "hello";
        return new GatewayContext(http, "r", new ApiInfo("orders", "/orders"), new OperationInfo("create", "POST", "/"))
        {
            Subscription = new SubscriptionInfo("sub-1", "gold", "premium"),
            Identity = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "tenant-9")], "test")),
        };
    }

    [Theory]
    [InlineData("static", "static")]
    [InlineData("{subscription.id}", "sub-1")]
    [InlineData("{subscription.product}/{subscription.plan}", "gold/premium")]
    [InlineData("t={claim:tid}", "t=tenant-9")]
    [InlineData("{api.name}.{operation.name}", "orders.create")]
    [InlineData("{request.method} {request.path}", "POST /orders/1")]
    [InlineData("{header:x-in}", "hello")]
    [InlineData("{unknown} {claim:missing}x", " x")]
    [InlineData("open { brace", "open { brace")]
    public void Evaluate_Template_ResolvesVariables(string template, string expected)
    {
        VariableTemplate.Parse(template).Evaluate(Context()).ShouldBe(expected);
    }

    [Fact]
    public void Evaluate_ContextVariable_IsResolved()
    {
        var context = Context();
        context.Variables["tier"] = "A";

        VariableTemplate.Parse("{var:tier}").Evaluate(context).ShouldBe("A");
    }

    [Fact]
    public void IsStatic_OnlyForLiteralTemplates()
    {
        VariableTemplate.Parse("abc").IsStatic.ShouldBeTrue();
        VariableTemplate.Parse("a{api.name}").IsStatic.ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_NoSubscription_ReturnsEmpty()
    {
        var context = new GatewayContext(new DefaultHttpContext(), "r", new ApiInfo("a", "/a"));

        VariableTemplate.Parse("[{subscription.id}]").Evaluate(context).ShouldBe("[]");
    }
}
