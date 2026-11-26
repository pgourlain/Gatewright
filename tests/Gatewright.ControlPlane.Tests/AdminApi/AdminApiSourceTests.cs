// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiSourceTests
{
    private static async Task<AdminApiHost> StartWithSourceApiAsync()
    {
        var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "payments" });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var api = new ApiEntity
        {
            Name = "shop.orders",
            BasePath = "/orders",
            Backend = new BackendDefinition { Service = "shop/orders:80" },
            Hosts = ["api.acme.com"],
            Source = "gatewayapi",
            SourceRef = "shop/orders",
            SourceProduct = "payments",
        };
        db.Apis.Add(api);
        db.Operations.Add(new OperationEntity { ApiId = api.Id, Name = "r0m0", Method = "GET", Path = "/{id}" });
        await db.SaveChangesAsync();
        return host;
    }

    private static ApiRequest Change() => new() { Name = "shop.orders", BasePath = "/changed", Backend = new BackendDefinition { Service = "shop/orders:80" } };

    [Fact]
    public async Task Get_ApiFromASource_ShowsItsSourceAndHosts()
    {
        await using var host = await StartWithSourceApiAsync();

        var api = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/shop.orders"));

        api.GetProperty("source").GetProperty("kind").GetString().ShouldBe("gatewayapi");
        api.GetProperty("source").GetProperty("ref").GetString().ShouldBe("shop/orders");
        api.GetProperty("source").GetProperty("product").GetString().ShouldBe("payments");
        api.GetProperty("hosts")[0].GetString().ShouldBe("api.acme.com");
    }

    [Fact]
    public async Task ChangingOrDeletingAnApiFromASource_Returns409()
    {
        await using var host = await StartWithSourceApiAsync();

        var put = await host.PutAsync("/admin/v1/apis/shop.orders", Change());
        var delete = await host.Client.DeleteAsync("/admin/v1/apis/shop.orders");

        put.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await put.Content.ReadAsStringAsync()).ShouldContain("detach");
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/shop.orders"))).GetProperty("basePath").GetString().ShouldBe("/orders");
    }

    [Fact]
    public async Task ChangingOrDeletingAnOperationOfAnApiFromASource_Returns409()
    {
        await using var host = await StartWithSourceApiAsync();

        var put = await host.PutAsync("/admin/v1/apis/shop.orders/operations/r0m0", new OperationRequest { Method = "POST", Path = "/x" });
        var delete = await host.Client.DeleteAsync("/admin/v1/apis/shop.orders/operations/r0m0");

        put.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Import_OverAnApiFromASource_IsRefused()
    {
        await using var host = await StartWithSourceApiAsync();
        const string document = "openapi: 3.0.3\ninfo: { title: shop.orders, version: \"1\" }\npaths:\n  /orders:\n    get: { operationId: list }\n";

        var response = await host.Client.PostAsync("/admin/v1/apis/import?name=shop.orders&service=shop/orders:80", new StringContent(document, Encoding.UTF8, "application/yaml"));

        (await response.Content.ReadAsStringAsync()).ShouldContain("detach");
    }

    [Fact]
    public async Task Detach_MakesTheApiManual_LinksItsProductAndIsAudited()
    {
        await using var host = await StartWithSourceApiAsync();

        var response = await host.Client.PostAsync("/admin/v1/apis/shop.orders/detach", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var api = await AdminApiHost.ReadAsync(response);
        api.TryGetProperty("source", out _).ShouldBeFalse();
        api.GetProperty("products")[0].GetString().ShouldBe("payments");
        (await host.PutAsync("/admin/v1/apis/shop.orders", Change() with { Products = ["payments"] })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        (await db.AuditEntries.AnyAsync(a => a.Action == "api.detach" && a.EntityId == "shop.orders")).ShouldBeTrue();
    }

    [Fact]
    public async Task Detach_ManualApiOrUnknownApi_IsRefused()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", new ApiRequest { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://o:80"] } });

        (await host.Client.PostAsync("/admin/v1/apis/orders/detach", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.Client.PostAsync("/admin/v1/apis/missing/detach", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
