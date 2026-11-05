// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;

namespace Gatewright.Core.Tests.Policies;

public sealed class PolicyChainResolverTests
{
    private static PolicyReference P(string type, PolicyInheritance inheritance = PolicyInheritance.Inherit, string? json = null) =>
        new()
        {
            Type = type,
            Inheritance = inheritance,
            Config = json is null ? null : JsonDocument.Parse(json).RootElement,
        };

    private static PolicySet Inbound(params PolicyReference[] policies) => new() { Inbound = policies };

    private static ApiDefinition Api(PolicySet policies, OperationDefinition? operation = null, params string[] products) => new()
    {
        Name = "orders",
        BasePath = "/orders",
        Backend = new BackendDefinition(),
        Products = products,
        Policies = policies,
        Operations = operation is null ? [] : [operation],
    };

    private static string[] Types(IReadOnlyList<ResolvedPolicy> chain) => [.. chain.Select(p => p.Type)];

    [Fact]
    public void Resolve_AllScopes_AppliesGlobalToOperationOrder()
    {
        var operation = new OperationDefinition { Name = "op", Method = "GET", Path = "/", Policies = Inbound(P("op")) };
        var config = new GatewayConfig
        {
            GlobalPolicies = Inbound(P("global")),
            GatewayPolicies = Inbound(P("gateway")),
            Products = [new ProductDefinition { Name = "gold", Policies = Inbound(P("product")) }],
        };
        var api = Api(Inbound(P("api")), operation, "gold");

        var chain = PolicyChainResolver.Resolve(config, api, operation, PolicyPhase.Inbound);

        Types(chain).ShouldBe(["global", "gateway", "product", "api", "op"]);
        chain.Select(p => p.Scope).ShouldBe(
            [PolicyScope.Global, PolicyScope.Gateway, PolicyScope.Product, PolicyScope.Api, PolicyScope.Operation]);
    }

    [Fact]
    public void Resolve_NoOperation_StopsAtApiScope()
    {
        var operation = new OperationDefinition { Name = "op", Method = "GET", Path = "/", Policies = Inbound(P("op")) };
        var api = Api(Inbound(P("api")), operation);

        Types(PolicyChainResolver.Resolve(new GatewayConfig(), api, null, PolicyPhase.Inbound)).ShouldBe(["api"]);
    }

    [Fact]
    public void Resolve_OverrideInChildScope_ReplacesInheritedPolicyInPlace()
    {
        var config = new GatewayConfig { GlobalPolicies = Inbound(P("jwt"), P("rateLimit", json: """{"limit":10}""")) };
        var api = Api(Inbound(P("rateLimit", PolicyInheritance.Override, """{"limit":99}"""), P("cors")));

        var chain = PolicyChainResolver.Resolve(config, api, null, PolicyPhase.Inbound);

        Types(chain).ShouldBe(["jwt", "rateLimit", "cors"]);
        chain[1].Scope.ShouldBe(PolicyScope.Api);
        chain[1].Config!.Value.GetProperty("limit").GetInt32().ShouldBe(99);
    }

    [Fact]
    public void Resolve_OverrideWithoutInherited_AppendsPolicy()
    {
        var api = Api(Inbound(P("cors"), P("cache", PolicyInheritance.Override)));

        Types(PolicyChainResolver.Resolve(new GatewayConfig(), api, null, PolicyPhase.Inbound)).ShouldBe(["cors", "cache"]);
    }

    [Fact]
    public void Resolve_RemoveInChildScope_DropsInheritedPolicy()
    {
        var config = new GatewayConfig { GlobalPolicies = Inbound(P("jwt"), P("cors")) };
        var operation = new OperationDefinition { Name = "op", Method = "GET", Path = "/", Policies = Inbound(P("jwt", PolicyInheritance.Remove)) };
        var api = Api(PolicySet.Empty, operation);

        var chain = PolicyChainResolver.Resolve(config, api, operation, PolicyPhase.Inbound);

        Types(chain).ShouldBe(["cors"]);
    }

    [Fact]
    public void Resolve_RemoveDoesNotAffectSiblingOperations()
    {
        var config = new GatewayConfig { GlobalPolicies = Inbound(P("jwt")) };
        var withRemove = new OperationDefinition { Name = "a", Method = "GET", Path = "/a", Policies = Inbound(P("jwt", PolicyInheritance.Remove)) };
        var other = new OperationDefinition { Name = "b", Method = "GET", Path = "/b" };
        var api = Api(PolicySet.Empty, withRemove);

        Types(PolicyChainResolver.Resolve(config, api, other, PolicyPhase.Inbound)).ShouldBe(["jwt"]);
    }

    [Fact]
    public void Resolve_SharedReference_ResolvesTypeAndConfig()
    {
        var config = new GatewayConfig
        {
            Policies = new Dictionary<string, PolicyReference> { ["std-limit"] = P("rateLimit", json: """{"limit":5}""") },
        };
        var api = Api(Inbound(new PolicyReference { Ref = "std-limit" }));

        var policy = PolicyChainResolver.Resolve(config, api, null, PolicyPhase.Inbound).ShouldHaveSingleItem();

        policy.Type.ShouldBe("rateLimit");
        policy.SharedName.ShouldBe("std-limit");
        policy.Config!.Value.GetProperty("limit").GetInt32().ShouldBe(5);
    }

    [Fact]
    public void Resolve_UnknownSharedReference_Throws()
    {
        var api = Api(Inbound(new PolicyReference { Ref = "missing" }));

        Should.Throw<InvalidOperationException>(
            () => PolicyChainResolver.Resolve(new GatewayConfig(), api, null, PolicyPhase.Inbound));
    }

    [Fact]
    public void Resolve_MultipleProducts_AppliesProductsInDeclaredOrder()
    {
        var config = new GatewayConfig
        {
            Products =
            [
                new ProductDefinition { Name = "a", Policies = Inbound(P("pa")) },
                new ProductDefinition { Name = "b", Policies = Inbound(P("pb")) },
            ],
        };
        var api = Api(PolicySet.Empty, null, "b", "a");

        Types(PolicyChainResolver.Resolve(config, api, null, PolicyPhase.Inbound)).ShouldBe(["pb", "pa"]);
    }

    [Fact]
    public void ResolveAll_PoliciesInSeveralPhases_ReturnsChainPerPhase()
    {
        var api = Api(new PolicySet { Inbound = [P("jwt")], Outbound = [P("headers")] });

        var all = PolicyChainResolver.ResolveAll(new GatewayConfig(), api, null);

        Types(all[PolicyPhase.Inbound]).ShouldBe(["jwt"]);
        Types(all[PolicyPhase.Outbound]).ShouldBe(["headers"]);
        all[PolicyPhase.Backend].ShouldBeEmpty();
        all[PolicyPhase.OnError].ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_CombinedPhaseFlag_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => PolicyChainResolver.Resolve(new GatewayConfig(), Api(PolicySet.Empty), null, PolicyPhase.Inbound | PolicyPhase.Outbound));
    }
}
