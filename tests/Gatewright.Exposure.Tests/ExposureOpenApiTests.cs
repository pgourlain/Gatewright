// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Configuration;
using Gatewright.Core.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatewright.Exposure.Tests;

[ApiController]
[Route("api/legacy")]
public sealed class LegacyController : ControllerBase
{
    [HttpGet]
    [GatewayRateLimit(25, 30)]
    [CacheOnGateway(15)]
    public IActionResult List() => Ok();

    [HttpPost]
    [GatewayPolicy("acme.fraudCheck", """{"maxAmount":500}""")]
    public IActionResult Create() => Ok();
}


public sealed class ExposureOpenApiTests
{
    private static async Task<string> GetDocumentAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(LegacyController).Assembly);
        builder.Services.AddOpenApi(options => options.AddGatewayExposure(g =>
        {
            g.Name = "orders";
            g.BasePath = "/orders";
            g.Products.Add("starter");
            g.Service = "shop/orders:80";
        }));
        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapControllers();
        app.MapGet("/orders", () => "list")
            .WithName("listOrders")
            .WithRateLimit(100, TimeSpan.FromMinutes(1))
            .CacheOnGateway(TimeSpan.FromSeconds(30));
        app.MapPost("/orders", () => "create")
            .WithName("createOrder")
            .WithGatewayPolicy("timeout", new { seconds = 5 }, phase: "backend")
            .WithGatewayPolicy("acme.audit", new { level = "full" });
        app.MapGet("/health", () => "ok").WithName("health");
        await app.StartAsync();
        using var client = app.GetTestClient();
        return await client.GetStringAsync("/openapi/v1.json");
    }

    [Fact]
    public async Task Document_DeclaresTheExtensionVersionAndDocumentLevelSettings()
    {
        var root = JsonDocument.Parse(await GetDocumentAsync()).RootElement;

        root.GetProperty("x-gateway-version").GetInt32().ShouldBe(1);
        var gateway = root.GetProperty("x-gateway");
        gateway.GetProperty("name").GetString().ShouldBe("orders");
        gateway.GetProperty("basePath").GetString().ShouldBe("/orders");
        gateway.GetProperty("products")[0].GetString().ShouldBe("starter");
        gateway.GetProperty("backend").GetProperty("service").GetString().ShouldBe("shop/orders:80");
    }

    [Fact]
    public async Task MinimalApiEndpoints_GetTheirPoliciesAsXGateway()
    {
        var paths = JsonDocument.Parse(await GetDocumentAsync()).RootElement.GetProperty("paths");

        var get = paths.GetProperty("/orders").GetProperty("get").GetProperty("x-gateway");
        get.GetProperty("rateLimit").GetProperty("limit").GetInt32().ShouldBe(100);
        get.GetProperty("rateLimit").GetProperty("periodSeconds").GetInt32().ShouldBe(60);
        get.GetProperty("cache").GetProperty("durationSeconds").GetInt32().ShouldBe(30);
        var post = paths.GetProperty("/orders").GetProperty("post").GetProperty("x-gateway");
        post.GetProperty("policies").GetProperty("backend")[0].GetProperty("type").GetString().ShouldBe("timeout");
        post.GetProperty("policies").GetProperty("inbound")[0].GetProperty("config").GetProperty("level").GetString().ShouldBe("full");
        paths.GetProperty("/health").GetProperty("get").TryGetProperty("x-gateway", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task MvcAttributes_AreSupportedToo()
    {
        var paths = JsonDocument.Parse(await GetDocumentAsync()).RootElement.GetProperty("paths");

        var get = paths.GetProperty("/api/legacy").GetProperty("get").GetProperty("x-gateway");
        get.GetProperty("rateLimit").GetProperty("limit").GetInt32().ShouldBe(25);
        get.GetProperty("cache").GetProperty("durationSeconds").GetInt32().ShouldBe(15);
        var post = paths.GetProperty("/api/legacy").GetProperty("post").GetProperty("x-gateway");
        post.GetProperty("policies").GetProperty("inbound")[0].GetProperty("type").GetString().ShouldBe("acme.fraudCheck");
    }

    [Fact]
    public async Task GeneratedDocument_IsReadByTheGatewayImporter()
    {
        var result = OpenApiImporter.Import(await GetDocumentAsync());

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        var api = result.Api!;
        api.Name.ShouldBe("orders");
        api.BasePath.ShouldBe("/orders");
        api.Backend.Service.ShouldBe("shop/orders:80");
        api.Products.ShouldBe(["starter"]);
        var list = api.Operations.Single(o => o.Name == "listOrders");
        list.Policies.Inbound.Select(p => p.Type).ShouldBe(["rateLimit", "cache"]);
        var create = api.Operations.Single(o => o.Name == "createOrder");
        create.Policies.Backend.Select(p => p.Type).ShouldBe(["timeout"]);
        create.Policies.Inbound.Select(p => p.Type).ShouldBe(["acme.audit"]);
        api.Operations.Single(o => o.Name == "health").Policies.ShouldBe(PolicySet.Empty);
    }
}
