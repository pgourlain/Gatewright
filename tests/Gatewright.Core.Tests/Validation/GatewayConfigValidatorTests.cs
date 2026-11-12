// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Validation;

namespace Gatewright.Core.Tests.Validation;

public sealed class GatewayConfigValidatorTests
{
    private sealed class KnownPolicy : IGatewayPolicy
    {
        public string Type => "known";

        public PolicyPhase Phases => PolicyPhase.Inbound;

        public JsonElement ConfigSchema { get; } = JsonElement.Parse("{}");

        public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context) => throw new NotSupportedException();
    }

    private static BackendDefinition Backend() => new() { Destinations = ["http://orders:8080"] };

    private static ApiDefinition Api(string name = "orders", string basePath = "/orders", params OperationDefinition[] operations) => new()
    {
        Name = name,
        BasePath = basePath,
        Backend = Backend(),
        Operations = operations,
    };

    private static OperationDefinition Op(string name, string method = "GET", string path = "/") =>
        new() { Name = name, Method = method, Path = path };

    private static string[] Codes(GatewayConfig config, PolicyRegistry? registry = null) =>
        [.. new GatewayConfigValidator(registry).Validate(config).Select(e => e.Code)];

    [Fact]
    public void Validate_ValidConfig_ReturnsNoError()
    {
        var config = new GatewayConfig
        {
            Products = [new ProductDefinition { Name = "gold" }],
            Apis = [Api(operations: [Op("get", "GET", "/{id}"), Op("create", "POST")]) with { Products = ["gold"] }],
        };

        Codes(config).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_DuplicateApiName_IsReported()
    {
        Codes(new GatewayConfig { Apis = [Api("a", "/a"), Api("a", "/b")] }).ShouldContain("api.duplicate-name");
    }

    [Fact]
    public void Validate_SameBasePathIgnoringCaseAndTrailingSlash_IsConflict()
    {
        Codes(new GatewayConfig { Apis = [Api("a", "/Orders/"), Api("b", "/orders")] }).ShouldContain("api.path-conflict");
    }

    [Fact]
    public void Validate_NestedBasePaths_AreNotConflicts()
    {
        Codes(new GatewayConfig { Apis = [Api("a", "/orders"), Api("b", "/orders/admin")] }).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_BasePathWithoutLeadingSlash_IsReported()
    {
        Codes(new GatewayConfig { Apis = [Api("a", "orders")] }).ShouldContain("api.base-path-invalid");
    }

    [Fact]
    public void Validate_UnknownProduct_IsReported()
    {
        Codes(new GatewayConfig { Apis = [Api() with { Products = ["ghost"] }] }).ShouldContain("api.unknown-product");
    }

    [Fact]
    public void Validate_DuplicateProductName_IsReported()
    {
        var config = new GatewayConfig { Products = [new ProductDefinition { Name = "p" }, new ProductDefinition { Name = "p" }] };

        Codes(config).ShouldContain("product.duplicate-name");
    }

    [Fact]
    public void Validate_DuplicateOperationNameAndRoute_AreReported()
    {
        var codes = Codes(new GatewayConfig
        {
            Apis = [Api(operations: [Op("x", "GET", "/{id}"), Op("x", "GET", "/{orderId}")])],
        });

        codes.ShouldContain("operation.duplicate-name");
        codes.ShouldContain("operation.path-conflict");
    }

    [Fact]
    public void Validate_InvalidMethod_IsReported()
    {
        Codes(new GatewayConfig { Apis = [Api(operations: [Op("x", "FETCH")])] }).ShouldContain("operation.method-invalid");
    }

    [Theory]
    [InlineData("ftp://host")]
    [InlineData("not a url")]
    public void Validate_InvalidDestination_IsReported(string destination)
    {
        var api = Api() with { Backend = new BackendDefinition { Destinations = [destination] } };

        Codes(new GatewayConfig { Apis = [api] }).ShouldContain("backend.destination-invalid");
    }

    [Fact]
    public void Validate_BackendWithoutTarget_IsReported()
    {
        var api = Api() with { Backend = new BackendDefinition() };

        Codes(new GatewayConfig { Apis = [api] }).ShouldContain("backend.target");
    }

    [Fact]
    public void Validate_BackendWithBothTargets_IsReported()
    {
        var api = Api() with { Backend = new BackendDefinition { Destinations = ["http://a"], Service = "ns/svc:80" } };

        Codes(new GatewayConfig { Apis = [api] }).ShouldContain("backend.target");
    }

    [Fact]
    public void Validate_ServiceReference_FormatChecked()
    {
        var good = Api() with { Backend = new BackendDefinition { Service = "shop/orders:8080" } };
        var bad = Api("b", "/b") with { Backend = new BackendDefinition { Service = "orders" } };

        Codes(new GatewayConfig { Apis = [good] }).ShouldBeEmpty();
        Codes(new GatewayConfig { Apis = [bad] }).ShouldContain("backend.service-invalid");
    }

    [Fact]
    public void Validate_PolicyProblems_AreReported()
    {
        var api = Api() with
        {
            Policies = new PolicySet
            {
                Inbound =
                [
                    new PolicyReference { Ref = "ghost" },
                    new PolicyReference(),
                    new PolicyReference { Type = "known", Ref = "x" },
                    new PolicyReference { Type = "mystery" },
                ],
            },
        };
        var registry = new PolicyRegistry([new KnownPolicy()]);

        var codes = Codes(new GatewayConfig { Apis = [api] }, registry);

        codes.ShouldContain("policy.unknown-reference");
        codes.ShouldContain("policy.type-required");
        codes.ShouldContain("policy.ambiguous");
        codes.ShouldContain("policy.unknown-type");
    }

    [Fact]
    public void Validate_UnknownTypeWithoutRegistry_IsNotReported()
    {
        var api = Api() with { Policies = new PolicySet { Inbound = [new PolicyReference { Type = "mystery" }] } };

        Codes(new GatewayConfig { Apis = [api] }).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_SeveralProblems_AreAllAggregated()
    {
        var config = new GatewayConfig
        {
            Apis = [Api("a", "x") with { Products = ["ghost"] }, Api("a", "/b") with { Backend = new BackendDefinition() }],
        };

        Codes(config).Length.ShouldBeGreaterThanOrEqualTo(4);
    }
}
