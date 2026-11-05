// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using Gatewright.ControlPlane.AdminApi;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiOpenApiImportTests
{
    private const string Document = """
        openapi: 3.0.3
        info: { title: Orders, version: "1" }
        paths:
          /orders:
            get:
              operationId: list
              x-gateway: { rateLimit: { limit: 10 } }
            post:
              operationId: create
          /orders/{id}:
            get:
              operationId: get
        """;

    private static Task<HttpResponseMessage> ImportAsync(AdminApiHost host, string document, string query = "?service=shop/orders:80&product=starter")
    {
        var content = new StringContent(document, Encoding.UTF8, "application/yaml");
        return host.Client.PostAsync("/admin/v1/apis/import" + query, content);
    }

    private static async Task<AdminApiHost> StartAsync()
    {
        var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "starter" });
        return host;
    }

    [Fact]
    public async Task Import_NewApi_IsCreatedWithItsOperationsAndPolicies()
    {
        await using var host = await StartAsync();

        var response = await ImportAsync(host, Document);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await AdminApiHost.ReadAsync(response);
        report.GetProperty("created").GetBoolean().ShouldBeTrue();
        report.GetProperty("applied").GetBoolean().ShouldBeTrue();
        report.GetProperty("added").GetArrayLength().ShouldBe(3);
        var api = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders"));
        api.GetProperty("basePath").GetString().ShouldBe("/orders");
        api.GetProperty("backend").GetProperty("service").GetString().ShouldBe("shop/orders:80");
        api.GetProperty("products")[0].GetString().ShouldBe("starter");
        api.GetProperty("operations").GetArrayLength().ShouldBe(3);
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/revisions")))[0].GetProperty("summary").GetString()!.ShouldContain("api.import");
    }

    [Fact]
    public async Task Import_Twice_ReportsNothingChanged()
    {
        await using var host = await StartAsync();
        await ImportAsync(host, Document);

        var report = await AdminApiHost.ReadAsync(await ImportAsync(host, Document));

        report.GetProperty("created").GetBoolean().ShouldBeFalse();
        report.GetProperty("added").GetArrayLength().ShouldBe(0);
        report.GetProperty("updated").GetArrayLength().ShouldBe(0);
        report.GetProperty("removed").GetArrayLength().ShouldBe(0);
        report.GetProperty("unchanged").GetInt32().ShouldBe(3);
        report.GetProperty("apiChanges").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Import_ChangedDocument_ReportsAddedUpdatedAndRemovedOperations()
    {
        await using var host = await StartAsync();
        await ImportAsync(host, Document);
        var changed = """
            openapi: 3.0.3
            info: { title: Orders, version: "2" }
            paths:
              /orders:
                get:
                  operationId: list
                  x-gateway: { rateLimit: { limit: 99 } }
              /orders/{id}:
                get:
                  operationId: get
              /orders/{id}/items:
                get:
                  operationId: items
            """;

        var report = await AdminApiHost.ReadAsync(await ImportAsync(host, changed));

        report.GetProperty("added").EnumerateArray().Select(e => e.GetString()).ShouldBe(["GET /orders/{id}/items (items)"]);
        report.GetProperty("updated").EnumerateArray().Select(e => e.GetString()).ShouldBe(["GET /orders (list)"]);
        report.GetProperty("removed").EnumerateArray().Select(e => e.GetString()).ShouldBe(["POST /orders (create)"]);
        report.GetProperty("unchanged").GetInt32().ShouldBe(1);
        report.GetProperty("apiChanges").EnumerateArray().Select(e => e.GetString()).ShouldBe(["version"]);
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders"))).GetProperty("operations").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task Import_RemoveMissingFalse_KeepsOperationsMissingFromTheDocument()
    {
        await using var host = await StartAsync();
        await ImportAsync(host, Document);
        var smaller = "openapi: 3.0.3\ninfo: { title: Orders, version: \"1\" }\npaths:\n  /orders:\n    get:\n      operationId: list\n      x-gateway: { rateLimit: { limit: 10 } }\n";

        await ImportAsync(host, smaller, "?service=shop/orders:80&product=starter&removeMissing=false");

        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders"))).GetProperty("operations").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task Import_DryRun_ReportsWithoutChangingTheDraft()
    {
        await using var host = await StartAsync();

        var report = await AdminApiHost.ReadAsync(await ImportAsync(host, Document, "?service=shop/orders:80&dryRun=true"));

        report.GetProperty("applied").GetBoolean().ShouldBeFalse();
        report.GetProperty("added").GetArrayLength().ShouldBe(3);
        (await host.Client.GetAsync("/admin/v1/apis/orders")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/revisions"))).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Import_Problems_AreReportedAs400()
    {
        await using var host = await StartAsync();

        (await ImportAsync(host, Document, "")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ImportAsync(host, Document, "?service=shop/orders:80&product=ghost")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ImportAsync(host, "{\"swagger\":\"2.0\"}", "?service=a/b:80")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_RequiresTheAdminRole()
    {
        await using var host = await StartAsync();
        host.Authenticate(AdminRoles.Publisher);

        (await ImportAsync(host, Document)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
