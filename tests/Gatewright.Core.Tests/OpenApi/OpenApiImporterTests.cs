// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.OpenApi;

namespace Gatewright.Core.Tests.OpenApi;

public sealed class OpenApiImporterTests
{
    private const string Yaml = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.2"
        servers:
          - url: https://api.example.com/orders/v1
        x-gateway-version: 1
        x-gateway:
          product: starter
          backend:
            service: shop/orders:80
          policies:
            inbound:
              - type: jwt
                config: { issuers: [] }
        paths:
          /orders:
            get:
              operationId: listOrders
              x-gateway:
                rateLimit: { limit: 100, periodSeconds: 60 }
                cache: { durationSeconds: 30 }
            post:
              operationId: createOrder
              x-gateway:
                timeout: { seconds: 5 }
                retry: { maxAttempts: 2 }
          /orders/{id}:
            get:
              summary: Get one
            delete:
              operationId: deleteOrder
        """;

    private static readonly BackendDefinition Backend = new() { Destinations = ["http://orders:8080"] };

    [Fact]
    public void Import_Yaml_ProducesApiOperationsAndPolicies()
    {
        var result = OpenApiImporter.Import(Yaml);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        var api = result.Api!;
        api.Name.ShouldBe("orders-api");
        api.BasePath.ShouldBe("/orders/v1");
        api.Version.ShouldBe("1.2");
        api.Backend.Service.ShouldBe("shop/orders:80");
        api.Products.ShouldBe(["starter"]);
        result.DisplayName.ShouldBe("Orders API");
        api.Policies.Inbound.ShouldHaveSingleItem().Type.ShouldBe("jwt");
        api.Operations.Select(o => $"{o.Method} {o.Path} {o.Name}").ShouldBe(
            ["GET /orders listOrders", "POST /orders createOrder", "DELETE /orders/{id} deleteOrder", "GET /orders/{id} get-orders-id"],
            ignoreOrder: true);
    }

    [Fact]
    public void Import_OperationShortcuts_BecomePoliciesInTheRightPhase()
    {
        var api = OpenApiImporter.Import(Yaml).Api!;

        var list = api.Operations.Single(o => o.Name == "listOrders").Policies;
        list.Inbound.Select(p => p.Type).ShouldBe(["rateLimit", "cache"]);
        list.Inbound[0].Config!.Value.GetProperty("limit").GetInt32().ShouldBe(100);
        var create = api.Operations.Single(o => o.Name == "createOrder").Policies;
        create.Backend.Select(p => p.Type).ShouldBe(["timeout", "retry"]);
        create.Inbound.ShouldBeEmpty();
    }

    [Fact]
    public void Import_OpenApi32Document_IsAccepted()
    {
        // ASP.NET Core 11 generates OpenAPI 3.2 by default.
        var json = """
            {"openapi":"3.2.0","info":{"title":"x","version":"1"},"x-gateway":{"backend":{"destinations":["http://b"]}},"paths":{"/items":{"get":{"operationId":"listItems"}}}}
            """;

        var result = OpenApiImporter.Import(json);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Api!.Operations.Select(o => $"{o.Method} {o.Path} {o.Name}").ShouldBe(["GET /items listItems"]);
    }

    [Fact]
    public void Import_CorsShortcut_AppliesToInboundAndOutbound()
    {
        var json = """
            {"openapi":"3.1.0","info":{"title":"x","version":"1"},"x-gateway":{"backend":{"destinations":["http://b"]},"cors":{"origins":["*"]}},"paths":{}}
            """;

        var api = OpenApiImporter.Import(json).Api!;

        api.Policies.Inbound.ShouldHaveSingleItem().Type.ShouldBe("cors");
        api.Policies.Outbound.ShouldHaveSingleItem().Type.ShouldBe("cors");
    }

    [Fact]
    public void Import_OptionsTakePrecedenceOverTheDocument()
    {
        var result = OpenApiImporter.Import(Yaml, new OpenApiImportOptions
        {
            Name = "orders",
            BasePath = "/o",
            Backend = Backend,
            Products = ["gold"],
        });

        var api = result.Api!;
        api.Name.ShouldBe("orders");
        api.BasePath.ShouldBe("/o");
        api.Backend.Destinations.ShouldBe(["http://orders:8080"]);
        api.Products.ShouldBe(["gold"]);
    }

    [Fact]
    public void Import_BasePathFallbacks_ServerPathThenName()
    {
        var withoutServer = """{"openapi":"3.0.0","info":{"title":"My API","version":"1"},"paths":{}}""";

        OpenApiImporter.Import(withoutServer, new OpenApiImportOptions { Backend = Backend }).Api!.BasePath.ShouldBe("/my-api");
    }

    [Fact]
    public void Import_NoBackend_IsAnError()
    {
        var result = OpenApiImporter.Import("""{"openapi":"3.0.0","info":{"title":"x","version":"1"},"paths":{}}""");

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("No backend"));
    }

    [Theory]
    [InlineData("""{"swagger":"2.0","info":{"title":"x"}}""", "Unsupported OpenAPI version")]
    [InlineData("""{"openapi":"3.0.0","x-gateway-version":2,"info":{"title":"x"}}""", "x-gateway-version")]
    [InlineData("::: not yaml :::\n  - [", "not valid")]
    public void Import_UnsupportedOrBrokenDocuments_AreRefusedWithAClearMessage(string document, string expected)
    {
        var result = OpenApiImporter.Import(document, new OpenApiImportOptions { Backend = Backend });

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Import_DuplicateOperationIds_AreRenamedWithAWarning()
    {
        var json = """
            {"openapi":"3.0.0","info":{"title":"x","version":"1"},"paths":{"/a":{"get":{"operationId":"same"}},"/b":{"get":{"operationId":"same"}}}}
            """;

        var result = OpenApiImporter.Import(json, new OpenApiImportOptions { Backend = Backend });

        result.Api!.Operations.Select(o => o.Name).ShouldBe(["same", "same-2"]);
        result.Warnings.ShouldContain(w => w.Contains("used twice"));
    }

    [Fact]
    public void Import_UnknownXGatewayKey_IsAWarningNotAnError()
    {
        var json = """
            {"openapi":"3.0.0","info":{"title":"x","version":"1"},"x-gateway":{"flux":1,"backend":{"destinations":["http://b"]}},"paths":{}}
            """;

        var result = OpenApiImporter.Import(json);

        result.IsSuccess.ShouldBeTrue();
        result.Warnings.ShouldContain(w => w.Contains("flux"));
    }

    [Fact]
    public void Import_InvalidXGatewayValue_IsAnError()
    {
        var json = """
            {"openapi":"3.0.0","info":{"title":"x","version":"1"},"x-gateway":{"backend":"not-an-object"},"paths":{}}
            """;

        OpenApiImporter.Import(json).Errors.ShouldContain(e => e.Contains("x-gateway.backend"));
    }

    [Fact]
    public void Import_ImportedApiPassesTheGatewayValidator()
    {
        var api = OpenApiImporter.Import(Yaml).Api!;
        var config = new GatewayConfig { Products = [new ProductDefinition { Name = "starter" }], Apis = [api with { Backend = new BackendDefinition { Destinations = ["http://b:80"] } }] };

        new Gatewright.Core.Validation.GatewayConfigValidator().Validate(config).ShouldBeEmpty();
    }
}
