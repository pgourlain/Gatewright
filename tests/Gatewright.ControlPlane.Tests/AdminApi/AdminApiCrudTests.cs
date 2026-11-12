// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiCrudTests
{
    private static ApiRequest Api(string name = "orders", string basePath = "/orders", params string[] products) => new()
    {
        Name = name,
        BasePath = basePath,
        Backend = new BackendDefinition { Destinations = ["http://orders:8080"] },
        Products = [.. products],
    };

    [Fact]
    public async Task Api_CreateGetListUpdateDelete_FullCycle()
    {
        await using var host = await AdminApiHost.StartAsync();

        var created = await host.PostAsync("/admin/v1/apis", Api());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe("/admin/v1/apis/orders");

        var fetched = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders"));
        fetched.GetProperty("basePath").GetString().ShouldBe("/orders");
        fetched.GetProperty("backend").GetProperty("destinations")[0].GetString().ShouldBe("http://orders:8080");

        var updated = await host.PutAsync("/admin/v1/apis/orders", Api() with { BasePath = "/v2/orders", DisplayName = "Orders" });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(updated)).GetProperty("displayName").GetString().ShouldBe("Orders");

        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis"))).GetArrayLength().ShouldBe(1);
        (await host.Client.DeleteAsync("/admin/v1/apis/orders")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Client.GetAsync("/admin/v1/apis/orders")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_DuplicateName_Returns409()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api());

        (await host.PostAsync("/admin/v1/apis", Api())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("", "/x")]
    [InlineData("bad name!", "/x")]
    [InlineData("ok", "no-slash")]
    public async Task Api_InvalidInput_Returns400WithFieldErrors(string name, string basePath)
    {
        await using var host = await AdminApiHost.StartAsync();

        var response = await host.PostAsync("/admin/v1/apis", Api(name, basePath));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AdminApiHost.ReadAsync(response)).GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Api_UnknownProduct_Returns400()
    {
        await using var host = await AdminApiHost.StartAsync();

        (await host.PostAsync("/admin/v1/apis", Api(products: "ghost"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Api_LinkedToProduct_ShowsOnBothSides()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold" });

        await host.PostAsync("/admin/v1/apis", Api(products: "gold"));

        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/products/gold"))).GetProperty("apis")[0].GetString().ShouldBe("orders");
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders"))).GetProperty("products")[0].GetString().ShouldBe("gold");
    }

    [Fact]
    public async Task Operations_UpsertListDelete()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api());

        var created = await host.PutAsync("/admin/v1/apis/orders/operations/getOrder", new OperationRequest { Method = "get", Path = "/{id}" });
        var updated = await host.PutAsync("/admin/v1/apis/orders/operations/getOrder", new OperationRequest { Method = "GET", Path = "/orders/{id}" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/apis/orders/operations"));
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("method").GetString().ShouldBe("GET");
        list[0].GetProperty("path").GetString().ShouldBe("/orders/{id}");
        (await host.Client.DeleteAsync("/admin/v1/apis/orders/operations/getOrder")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Client.DeleteAsync("/admin/v1/apis/orders/operations/getOrder")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operations_InvalidMethodOrPath_Returns400()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api());

        (await host.PutAsync("/admin/v1/apis/orders/operations/x", new OperationRequest { Method = "FETCH", Path = "/" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PutAsync("/admin/v1/apis/orders/operations/x", new OperationRequest { Method = "GET", Path = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PutAsync("/admin/v1/apis/ghost/operations/x", new OperationRequest { Method = "GET", Path = "/" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProductsAndPlans_CrudAndConflicts()
    {
        await using var host = await AdminApiHost.StartAsync();
        (await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold", RequiresSubscription = true })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.PutAsync("/admin/v1/products/gold/plans/premium", new PlanRequest { Description = "best" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PutAsync("/admin/v1/products/gold", new ProductRequest { RequiresSubscription = false })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var plans = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/products/gold/plans"));
        plans[0].GetProperty("name").GetString().ShouldBe("premium");
        (await host.Client.DeleteAsync("/admin/v1/products/gold/plans/premium")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Client.DeleteAsync("/admin/v1/products/gold")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Subscriptions_CrudAndReferentialIntegrity()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold" });
        await host.PutAsync("/admin/v1/products/gold/plans/premium", new PlanRequest());
        await host.PostAsync("/admin/v1/consumers", new ConsumerRequest { Name = "acme", Contact = "ops@acme.example" });

        var created = await host.PostAsync("/admin/v1/subscriptions", new SubscriptionRequest { Name = "acme-gold", Consumer = "acme", Product = "gold", Plan = "premium" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AdminApiHost.ReadAsync(created);
        body.GetProperty("plan").GetString().ShouldBe("premium");
        body.GetProperty("active").GetBoolean().ShouldBeTrue();
        (await host.PostAsync("/admin/v1/subscriptions", new SubscriptionRequest { Name = "x", Consumer = "ghost", Product = "gold" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PostAsync("/admin/v1/subscriptions", new SubscriptionRequest { Name = "x", Consumer = "acme", Product = "gold", Plan = "nope" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.Client.DeleteAsync("/admin/v1/products/gold")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.Client.DeleteAsync("/admin/v1/consumers/acme")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.Client.DeleteAsync("/admin/v1/products/gold/plans/premium")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.PutAsync("/admin/v1/subscriptions/acme-gold", new SubscriptionRequest { Consumer = "acme", Product = "gold", Active = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.Client.DeleteAsync("/admin/v1/subscriptions/acme-gold")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Client.DeleteAsync("/admin/v1/consumers/acme")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SharedPoliciesAndScopePolicies_AreStoredAndReturned()
    {
        await using var host = await AdminApiHost.StartAsync();
        var shared = new PolicyReference { Type = "rateLimit", Config = System.Text.Json.JsonElement.Parse("""{"limit":10}""") };

        (await host.PutAsync("/admin/v1/policies/default-limit", shared)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PutAsync("/admin/v1/policies/default-limit", shared)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.PutAsync("/admin/v1/policies/bad", new PolicyReference { Ref = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var scopes = new ScopePoliciesDto { Global = new PolicySet { Inbound = [new PolicyReference { Ref = "default-limit" }] } };
        (await host.PutAsync("/admin/v1/scope-policies", scopes)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/policies")))
            .GetProperty("default-limit").GetProperty("config").GetProperty("limit").GetInt32().ShouldBe(10);
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/scope-policies")))
            .GetProperty("global").GetProperty("inbound")[0].GetProperty("ref").GetString().ShouldBe("default-limit");
        (await host.Client.DeleteAsync("/admin/v1/policies/default-limit")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OpenApiDocument_IsPublishedForTheAdminApi()
    {
        await using var host = await AdminApiHost.StartAsync();

        var response = await host.Client.GetAsync("/admin/v1/openapi.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paths = (await AdminApiHost.ReadAsync(response)).GetProperty("paths");
        paths.TryGetProperty("/admin/v1/apis", out _).ShouldBeTrue();
        paths.TryGetProperty("/admin/v1/environments/{env}/publications", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Hostnames_CrudValidationAndExport()
    {
        await using var host = await AdminApiHost.StartAsync();
        var request = new HostnameRequest
        {
            Certificate = new CertificateSource { File = new CertificateFiles("/tls/tls.crt", "/tls/tls.key") },
            RedirectHttpToHttps = true,
            Hsts = new HstsDefinition { MaxAgeSeconds = 600, IncludeSubDomains = true },
        };

        (await host.PutAsync("/admin/v1/hostnames/api.example.com", request)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PutAsync("/admin/v1/hostnames/api.example.com", request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.PutAsync("/admin/v1/hostnames/*.example.com", new HostnameRequest { Certificate = new CertificateSource { Secret = "shop/tls" } })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PutAsync("/admin/v1/hostnames/bad host", request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PutAsync("/admin/v1/hostnames/both.example.com", new HostnameRequest { Certificate = new CertificateSource { Secret = "a/b", File = new CertificateFiles("c", "k") } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var list = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/hostnames"));
        list.GetArrayLength().ShouldBe(2);
        var draft = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/draft"));
        var exported = draft.GetProperty("hostnames").EnumerateArray().Single(h => h.GetProperty("name").GetString() == "api.example.com");
        exported.GetProperty("redirectHttpToHttps").GetBoolean().ShouldBeTrue();
        exported.GetProperty("hsts").GetProperty("maxAgeSeconds").GetInt32().ShouldBe(600);
        (await host.Client.DeleteAsync("/admin/v1/hostnames/api.example.com")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Client.DeleteAsync("/admin/v1/hostnames/api.example.com")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PolicyTypes_ListsTheRegisteredTypesWithSchemas()
    {
        await using var host = await AdminApiHost.StartAsync();

        var types = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/policy-types"));

        var rateLimit = types.EnumerateArray().Single(t => t.GetProperty("type").GetString() == "rateLimit");
        rateLimit.GetProperty("phases")[0].GetString().ShouldBe("Inbound");
        rateLimit.GetProperty("schema").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
    }
}
