// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiPublishingTests
{
    private static ApiRequest Api(string name, string basePath) => new()
    {
        Name = name,
        BasePath = basePath,
        Backend = new BackendDefinition { Destinations = ["http://backend:80"] },
    };

    [Fact]
    public async Task EveryMutation_CreatesARevision()
    {
        await using var host = await AdminApiHost.StartAsync();

        await host.PostAsync("/admin/v1/apis", Api("orders", "/orders"));
        await host.PostAsync("/admin/v1/apis", Api("invoices", "/invoices"));
        await host.PutAsync("/admin/v1/apis/orders", Api("orders", "/v2/orders"));

        var revisions = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/revisions"));
        revisions.GetArrayLength().ShouldBe(3);
        revisions[0].GetProperty("number").GetInt64().ShouldBe(3);
        revisions[0].GetProperty("createdBy").GetString().ShouldBe("alice@example.com");
        revisions[0].GetProperty("summary").GetString()!.ShouldContain("api.update");
    }

    [Fact]
    public async Task PublishRollbackAndDiff_FullLifecycle()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api("orders", "/orders"));
        var first = await host.PostAsync("/admin/v1/environments/prod/publications", new PublishRequest());
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(first)).GetProperty("number").GetInt64().ShouldBe(1);
        await host.PostAsync("/admin/v1/apis", Api("invoices", "/invoices"));
        await host.PostAsync("/admin/v1/environments/prod/publications", new PublishRequest { Revision = 2 });

        var current = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/environments/prod/current"));
        current.GetProperty("version").GetInt64().ShouldBe(2);
        current.GetProperty("apis").GetArrayLength().ShouldBe(2);

        var rollback = await host.PostAsync("/admin/v1/environments/prod/rollback", new PublishRequest { Revision = 1 });
        rollback.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminApiHost.ReadAsync(rollback)).GetProperty("kind").GetString().ShouldBe("Rollback");
        (await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/environments/prod/current"))).GetProperty("apis").GetArrayLength().ShouldBe(1);

        var publications = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/environments/prod/publications"));
        publications.EnumerateArray().Select(p => p.GetProperty("number").GetInt64()).ShouldBe([3, 2, 1]);

        var diff = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/revisions/1/diff/2"));
        diff.EnumerateArray().Any(d => d.GetProperty("kind").GetString() == "Added" && d.GetProperty("path").GetString() == "apis.invoices").ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_InvalidConfiguration_Returns400WithErrorsAndKeepsEnvironmentUntouched()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api("a", "/same"));
        await host.PostAsync("/admin/v1/apis", Api("b", "/same"));

        var response = await host.PostAsync("/admin/v1/environments/dev/publications", new PublishRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("api.path-conflict");
        (await host.Client.GetAsync("/admin/v1/environments/dev/current")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Validate_Draft_ReportsErrorsWithoutPublishing()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api("a", "/same"));
        await host.PostAsync("/admin/v1/apis", Api("b", "/same"));

        var result = await AdminApiHost.ReadAsync(await host.Client.PostAsync("/admin/v1/validate", null));

        result.GetProperty("valid").GetBoolean().ShouldBeFalse();
        result.GetProperty("errors")[0].GetString()!.ShouldContain("api.path-conflict");
    }

    [Fact]
    public async Task Validate_SubmittedConfig_IsCheckedInsteadOfTheDraft()
    {
        await using var host = await AdminApiHost.StartAsync();
        using var content = new StringContent("""{"apis":[{"name":"x","basePath":"/x","backend":{"destinations":["http://x"]}}]}""", System.Text.Encoding.UTF8, "application/json");

        var result = await AdminApiHost.ReadAsync(await host.Client.PostAsync("/admin/v1/validate", content));

        result.GetProperty("valid").GetBoolean().ShouldBeTrue();
        result.GetProperty("hash").GetString()!.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Rollback_Errors_AreReported()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api("orders", "/orders"));

        (await host.PostAsync("/admin/v1/environments/prod/rollback", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PostAsync("/admin/v1/environments/prod/rollback", new PublishRequest { Revision = 1 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await host.PostAsync("/admin/v1/environments/nowhere/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.PostAsync("/admin/v1/environments/prod/publications", new PublishRequest { Revision = 99 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Environments_ListCurrentPublicationNumber()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api("orders", "/orders"));
        await host.PostAsync("/admin/v1/environments/staging/publications", new PublishRequest());

        var environments = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/environments"));

        var byName = environments.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        byName.Keys.ShouldBe(["dev", "prod", "staging"], ignoreOrder: true);
        byName["staging"].GetProperty("currentPublication").GetInt64().ShouldBe(1);
        byName["prod"].TryGetProperty("currentPublication", out _).ShouldBeFalse();
    }
}
