// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using System.Text.Json;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiExportImportTests
{
    private static async Task PopulateAsync(AdminApiHost host)
    {
        await host.PostAsync("/admin/v1/products", new ProductRequest { Name = "gold", RequiresSubscription = true });
        await host.PutAsync("/admin/v1/products/gold/plans/premium", new PlanRequest { Description = "best plan" });
        await host.PostAsync("/admin/v1/consumers", new ConsumerRequest { Name = "acme", Contact = "ops@acme.example" });
        await host.PostAsync(
            "/admin/v1/apis",
            new ApiRequest
            {
                Name = "orders",
                DisplayName = "Orders API",
                BasePath = "/orders",
                Version = "v1",
                Backend = new BackendDefinition { Destinations = ["http://orders:8080"], Timeout = TimeSpan.FromSeconds(5) },
                Products = ["gold"],
            });
        await host.PutAsync("/admin/v1/apis/orders/operations/getOrder", new OperationRequest { Method = "GET", Path = "/{id}" });
        await host.PutAsync("/admin/v1/policies/default-limit", new PolicyReference { Type = "rateLimit", Config = JsonElement.Parse("""{"limit":10}""") });
        await host.PutAsync("/admin/v1/scope-policies", new ScopePoliciesDto { Global = new PolicySet { Inbound = [new PolicyReference { Ref = "default-limit" }] } });
        await host.PostAsync("/admin/v1/subscriptions", new SubscriptionRequest { Name = "acme-gold", Consumer = "acme", Product = "gold", Plan = "premium" });
        await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/primary", null);
        await host.Client.PostAsync("/admin/v1/subscriptions/acme-gold/keys/secondary", null);
    }

    private static async Task<string> HashOfDraftAsync(AdminApiHost host)
    {
        var result = await AdminApiHost.ReadAsync(await host.Client.PostAsync("/admin/v1/validate", null));
        result.GetProperty("valid").GetBoolean().ShouldBeTrue(result.ToString());
        return result.GetProperty("hash").GetString()!;
    }

    private static async Task<string> ExportAsync(AdminApiHost host, string query = "")
    {
        var response = await host.Client.GetAsync("/admin/v1/export" + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<HttpResponseMessage> ImportAsync(AdminApiHost host, string json, string query = "")
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return host.Client.PostAsync("/admin/v1/import" + query, content);
    }

    [Fact]
    public async Task ExportThenImportInEmptyDatabase_ProducesTheSameHash()
    {
        string document;
        string expectedHash;
        await using (var source = await AdminApiHost.StartAsync(new Dictionary<string, string?>()))
        {
            await PopulateAsync(source);
            document = await ExportAsync(source);
            expectedHash = await HashOfDraftAsync(source);
        }

        await using var target = await AdminApiHost.StartAsync();
        var response = await ImportAsync(target, document);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(response)).GetProperty("hash").GetString().ShouldBe(expectedHash);
        (await HashOfDraftAsync(target)).ShouldBe(expectedHash);
        (await ExportAsync(target)).ShouldBe(document);
    }

    [Fact]
    public async Task Import_RestoresCatalogDetailsAndKeys()
    {
        string document;
        await using (var source = await AdminApiHost.StartAsync())
        {
            await PopulateAsync(source);
            document = await ExportAsync(source);
        }

        await using var target = await AdminApiHost.StartAsync();
        await ImportAsync(target, document);

        (await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/apis/orders"))).GetProperty("displayName").GetString().ShouldBe("Orders API");
        (await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/consumers/acme"))).GetProperty("contact").GetString().ShouldBe("ops@acme.example");
        var subscription = await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/subscriptions/acme-gold"));
        subscription.GetProperty("plan").GetString().ShouldBe("premium");
        subscription.GetProperty("keys").GetArrayLength().ShouldBe(2);
        (await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/revisions")))[0].GetProperty("summary").GetString().ShouldBe("import");
    }

    [Fact]
    public async Task Import_ReplacesTheExistingDraft()
    {
        string document;
        await using (var source = await AdminApiHost.StartAsync())
        {
            await PopulateAsync(source);
            document = await ExportAsync(source);
        }

        await using var target = await AdminApiHost.StartAsync();
        await target.PostAsync("/admin/v1/apis", new ApiRequest { Name = "legacy", BasePath = "/legacy", Backend = new BackendDefinition { Destinations = ["http://l"] } });

        await ImportAsync(target, document);

        (await target.Client.GetAsync("/admin/v1/apis/legacy")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await target.Client.GetAsync("/admin/v1/apis/orders")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Import_InvalidDocument_IsRefusedAndDraftIsUntouched()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", new ApiRequest { Name = "keep", BasePath = "/keep", Backend = new BackendDefinition { Destinations = ["http://k"] } });
        var invalid = """{"format":"gatewright/v1","config":{"apis":[{"name":"a","basePath":"nope","backend":{}}]}}""";

        var response = await ImportAsync(host, invalid);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("api.base-path-invalid");
        (await host.Client.GetAsync("/admin/v1/apis/keep")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Import_UnsupportedFormat_IsRefused()
    {
        await using var host = await AdminApiHost.StartAsync();

        var response = await ImportAsync(host, """{"format":"other/v9","config":{}}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Unsupported format");
    }

    [Fact]
    public async Task Import_SubscriptionWithoutConsumer_IsRefused()
    {
        await using var host = await AdminApiHost.StartAsync();
        var json = """{"format":"gatewright/v1","config":{"products":[{"name":"p"}],"subscriptions":[{"id":"s","product":"p"}]}}""";

        var response = await ImportAsync(host, json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("import.consumer");
    }

    [Fact]
    public async Task Import_DryRun_ValidatesWithoutChangingAnything()
    {
        string document;
        await using (var source = await AdminApiHost.StartAsync())
        {
            await PopulateAsync(source);
            document = await ExportAsync(source);
        }

        await using var target = await AdminApiHost.StartAsync();
        var response = await ImportAsync(target, document, "?dryRun=true");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(response)).GetProperty("applied").GetBoolean().ShouldBeFalse();
        (await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/apis"))).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Promotion_ExportOfPublishedEnvironment_CanBeImportedElsewhereAndPublished()
    {
        string document;
        long sourceVersion;
        await using (var source = await AdminApiHost.StartAsync())
        {
            await PopulateAsync(source);
            await source.PostAsync("/admin/v1/environments/staging/publications", new PublishRequest());
            document = await ExportAsync(source, "?environment=staging");
            sourceVersion = JsonElement.Parse(document).GetProperty("config").GetProperty("version").GetInt64();
        }

        sourceVersion.ShouldBe(1);
        await using var target = await AdminApiHost.StartAsync();
        (await ImportAsync(target, document)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = await target.PostAsync("/admin/v1/environments/prod/publications", new PublishRequest());

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(await target.Client.GetAsync("/admin/v1/environments/prod/current"))).GetProperty("apis")[0].GetProperty("name").GetString().ShouldBe("orders");
    }

    [Fact]
    public async Task Export_EnvironmentWithoutPublication_IsNotFound()
    {
        await using var host = await AdminApiHost.StartAsync();

        (await host.Client.GetAsync("/admin/v1/export?environment=prod")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImportExport_RequireTheRightRoles()
    {
        await using var host = await AdminApiHost.StartAsync();
        host.Authenticate(AdminRoles.Publisher);

        (await ImportAsync(host, """{"format":"gatewright/v1","config":{}}""")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await host.Client.GetAsync("/admin/v1/export")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
