// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;

namespace Gatewright.Core.Tests.Snapshots;

public sealed class BlockedApiTests
{
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class PassPolicy : IGatewayPolicy
    {
        public string Type => "pass";

        public PolicyPhase Phases => PolicyPhase.Inbound;

        public JsonElement ConfigSchema { get; } = JsonElement.Parse("""{"type":"object"}""");

        public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context) => new PassInstance();
    }

    private sealed class PassInstance : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);
    }

    private static SnapshotTranslator Translator()
    {
        var registry = new PolicyRegistry([new PassPolicy()]);
        return new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, new NoServices()));
    }

    private static PolicySet PassSet() => new() { Inbound = [new PolicyReference { Type = "pass", Config = JsonElement.Parse("{}") }] };

    private static GatewayConfig Config(string? requestedProduct, IReadOnlyList<ProductDefinition> products, IReadOnlyList<string>? apiProducts = null, PolicySet? apiPolicies = null) => new()
    {
        Products = products,
        Apis =
        [
            new ApiDefinition
            {
                Name = "orders",
                BasePath = "/orders",
                Backend = new BackendDefinition { Service = "shop/orders:80" },
                Products = apiProducts ?? [],
                Policies = apiPolicies ?? PolicySet.Empty,
                Source = new ApiSource { Kind = ApiSource.GatewayApi, Ref = "shop/orders", Product = requestedProduct },
            },
        ],
    };

    private static async Task<string?> BlockedReasonAsync(SnapshotTranslationResult result)
    {
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        var pipeline = result.Snapshot!.Pipelines["orders"].Pipeline;
        var context = new GatewayContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), "orders", new ApiInfo("orders", "/orders", null), null, CancellationToken.None);
        var outcome = await pipeline.ExecuteAsync(context, PolicyPhase.Inbound);
        if (outcome.Kind == PolicyResultKind.Continue)
        {
            return null;
        }

        outcome.StatusCode.ShouldBe(503);
        outcome.Code.ShouldBe(BlockedApiPolicy.Code);
        outcome.Message!.ShouldNotContain("Product");
        return context.Variables[BlockedApiPolicy.ReasonVariable] as string;
    }

    [Fact]
    public async Task Translate_NoRequestedProduct_BlocksWithMissingProduct()
    {
        var result = Translator().Translate(Config(null, []));

        (await BlockedReasonAsync(result)).ShouldBe("MissingProduct");
    }

    [Fact]
    public async Task Translate_UnknownProduct_BlocksWithoutInvalidatingTheConfiguration()
    {
        var result = Translator().Translate(Config("payments", [new ProductDefinition { Name = "other" }]));

        (await BlockedReasonAsync(result)).ShouldBe("ProductNotFound");
    }

    [Fact]
    public async Task Translate_ProductWithoutPolicy_BlocksWithNoPolicy()
    {
        var result = Translator().Translate(Config("payments", [new ProductDefinition { Name = "payments" }], ["payments"]));

        (await BlockedReasonAsync(result)).ShouldBe("NoPolicy");
    }

    [Fact]
    public async Task Translate_ProductGetsAPolicy_Unblocks()
    {
        var product = new ProductDefinition { Name = "payments", Policies = PassSet() };

        (await BlockedReasonAsync(Translator().Translate(Config("payments", [product], ["payments"])))).ShouldBeNull();
    }

    [Fact]
    public async Task Translate_PolicyOnTheApiItself_Unblocks()
    {
        var result = Translator().Translate(Config("payments", [new ProductDefinition { Name = "payments" }], ["payments"], PassSet()));

        (await BlockedReasonAsync(result)).ShouldBeNull();
    }

    [Fact]
    public async Task Translate_GlobalPolicyOnly_StillBlocks()
    {
        var config = Config("payments", [new ProductDefinition { Name = "payments" }], ["payments"]) with { GlobalPolicies = PassSet() };

        (await BlockedReasonAsync(Translator().Translate(config))).ShouldBe("NoPolicy");
    }

    [Fact]
    public void Translate_BlockedApi_StaysARealRoute()
    {
        var snapshot = Translator().Translate(Config(null, [])).Snapshot!;

        snapshot.Routes.Select(r => r.RouteId).ShouldBe(["orders"]);
        snapshot.Routes[0].Match.Path.ShouldBe("/orders/{**remainder}");
    }

    [Fact]
    public async Task Translate_ApiDeclaredByHand_IsNeverBlocked()
    {
        var config = new GatewayConfig
        {
            Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Service = "shop/orders:80" } }],
        };

        (await BlockedReasonAsync(Translator().Translate(config))).ShouldBeNull();
    }

    [Fact]
    public void Translate_Hosts_BecomeTheRouteHosts()
    {
        var config = new GatewayConfig
        {
            Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Hosts = ["api.acme.com", "*.acme.org"], Backend = new BackendDefinition { Service = "shop/orders:80" } }],
        };

        Translator().Translate(config).Snapshot!.Routes[0].Match.Hosts.ShouldBe(["api.acme.com", "*.acme.org"]);
    }

    [Fact]
    public void Validate_SameBasePathOnDifferentHosts_IsNotAConflict()
    {
        ApiDefinition Api(string name, params string[] hosts) => new() { Name = name, BasePath = "/orders", Hosts = hosts.Length == 0 ? null : hosts, Backend = new BackendDefinition { Service = "shop/orders:80" } };
        var validator = new GatewayConfigValidator();

        validator.Validate(new GatewayConfig { Apis = [Api("a", "a.acme.com"), Api("b", "b.acme.com")] }).ShouldBeEmpty();
        validator.Validate(new GatewayConfig { Apis = [Api("a", "a.acme.com"), Api("b", "A.acme.com")] }).Select(e => e.Code).ShouldContain("api.path-conflict");
        validator.Validate(new GatewayConfig { Apis = [Api("a"), Api("b")] }).Select(e => e.Code).ShouldContain("api.path-conflict");
    }

    [Fact]
    public void Validate_InvalidHost_IsReported()
    {
        var config = new GatewayConfig
        {
            Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Hosts = ["bad host"], Backend = new BackendDefinition { Service = "shop/orders:80" } }],
        };

        new GatewayConfigValidator().Validate(config).Select(e => e.Code).ShouldContain("api.host-invalid");
    }
}
