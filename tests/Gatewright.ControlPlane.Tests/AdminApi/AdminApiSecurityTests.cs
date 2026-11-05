// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Tests.AdminApi;

public sealed class AdminApiSecurityTests
{
    private static ApiRequest Api() => new()
    {
        Name = "orders",
        BasePath = "/orders",
        Backend = new BackendDefinition { Destinations = ["http://b:80"] },
    };

    [Fact]
    public async Task NoToken_Returns401()
    {
        await using var host = await AdminApiHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = null;

        (await host.Client.GetAsync("/admin/v1/apis")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/admin/v1/openapi.json")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("api://other", "https://test-issuer.example", false)]
    [InlineData("api://gatewright-admin", "https://evil.example", false)]
    [InlineData("api://gatewright-admin", "https://test-issuer.example", true)]
    public async Task Token_WithWrongAudienceOrIssuerOrExpired_IsRejected(string audience, string issuer, bool expired)
    {
        await using var host = await AdminApiHost.StartAsync();
        var token = host.Token([AdminRoles.Admin], audience: audience, issuer: issuer, expires: expired ? DateTime.UtcNow.AddHours(-2) : null);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await host.Client.GetAsync("/admin/v1/apis")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithoutAnyRole_IsForbidden()
    {
        await using var host = await AdminApiHost.StartAsync();
        host.Authenticate();

        (await host.Client.GetAsync("/admin/v1/apis")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reader_CanReadButNotChangeOrPublish()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api());
        host.Authenticate(AdminRoles.Reader);

        (await host.Client.GetAsync("/admin/v1/apis")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.Client.GetAsync("/admin/v1/revisions")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.PostAsync("/admin/v1/apis", Api() with { Name = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await host.Client.DeleteAsync("/admin/v1/apis/orders")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await host.PostAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await host.Client.GetAsync("/admin/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publisher_CanPublishAndReadButNotEditTheCatalog()
    {
        await using var host = await AdminApiHost.StartAsync();
        await host.PostAsync("/admin/v1/apis", Api());
        host.Authenticate(AdminRoles.Publisher);

        (await host.PostAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.Client.GetAsync("/admin/v1/apis")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.PostAsync("/admin/v1/apis", Api() with { Name = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_CanDoEverything()
    {
        await using var host = await AdminApiHost.StartAsync();

        (await host.PostAsync("/admin/v1/apis", Api())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await host.PostAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.Client.GetAsync("/admin/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_RecordsWhoDidWhat_IncludingPublication()
    {
        await using var host = await AdminApiHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token([AdminRoles.Admin], actor: "carol@example.com"));
        await host.PostAsync("/admin/v1/apis", Api());
        await host.PostAsync("/admin/v1/environments/prod/publications", new PublishRequest());
        await host.Client.DeleteAsync("/admin/v1/apis/orders");

        var audit = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/audit"));

        var entries = audit.EnumerateArray().Select(e => (Action: e.GetProperty("action").GetString(), Actor: e.GetProperty("actor").GetString(), Id: e.GetProperty("entityId").GetString())).ToList();
        entries.ShouldContain(("api.create", "carol@example.com", "orders"));
        entries.ShouldContain(("publish", "carol@example.com", "prod"));
        entries.ShouldContain(("api.delete", "carol@example.com", "orders"));
        audit[0].GetProperty("timestamp").GetDateTimeOffset().ShouldBe(host.Time.GetUtcNow());
    }

    [Fact]
    public async Task DevelopmentBypass_OutsideDevelopment_RefusesToStart()
    {
        var act = () => AdminApiHost.StartAsync(new Dictionary<string, string?> { ["ControlPlane:Auth:DevelopmentBypass"] = "true" }, environment: "Production");

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("Development");
    }

    [Fact]
    public async Task DevelopmentBypass_InDevelopment_AcceptsEveryoneAsAdmin()
    {
        await using var host = await AdminApiHost.StartAsync(new Dictionary<string, string?> { ["ControlPlane:Auth:DevelopmentBypass"] = "true" }, environment: "Development");
        host.Client.DefaultRequestHeaders.Authorization = null;

        (await host.PostAsync("/admin/v1/apis", Api())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var audit = await AdminApiHost.ReadAsync(await host.Client.GetAsync("/admin/v1/audit"));
        audit[0].GetProperty("actor").GetString().ShouldBe("dev-user");
    }
}
