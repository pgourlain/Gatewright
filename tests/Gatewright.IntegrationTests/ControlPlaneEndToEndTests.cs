// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Json;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gatewright.IntegrationTests;

public sealed class ControlPlaneEndToEndTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-cp-").FullName;
    private WebApplication? _backend;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _backend = builder.Build();
        _backend.MapGet("/{**path}", (HttpContext ctx) => "backend:" + ctx.Request.Path);
        await _backend.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_backend is not null)
        {
            await _backend.DisposeAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Development");
        b.UseSetting("Gateway:Mode", "All");
        b.UseSetting("Gateway:ConfigSource", "Database");
        b.UseSetting("Gateway:Environment", "dev");
        b.UseSetting("Gateway:ConfigPollInterval", "00:00:00.100");
        b.UseSetting("ControlPlane:ConnectionString", $"Data Source={Path.Combine(_directory, "cp.db")}");
        b.UseSetting("ControlPlane:Auth:DevelopmentBypass", "true");
    });

    private static async Task<HttpStatusCode> WaitForAsync(HttpClient client, string url, HttpStatusCode expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpStatusCode last;
        do
        {
            last = (await client.GetAsync(url)).StatusCode;
            if (last == expected)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return last;
    }

    [Fact]
    public async Task PublishThroughAdminApi_GatewayStartsServingTheApi_AndFollowsRollback()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var api = new ApiRequest
        {
            Name = "orders",
            BasePath = "/orders",
            Backend = new BackendDefinition { Destinations = [_backend!.Urls.First()] },
        };
        (await client.PostAsJsonAsync("/admin/v1/apis", api)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.GetAsync("/orders/1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await client.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await WaitForAsync(client, "/orders/1", HttpStatusCode.OK)).ShouldBe(HttpStatusCode.OK);
        (await client.GetStringAsync("/orders/1")).ShouldBe("backend:/1");
        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/admin/v1/apis", api with { Name = "invoices", BasePath = "/invoices" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WaitForAsync(client, "/invoices/1", HttpStatusCode.OK)).ShouldBe(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/admin/v1/environments/dev/rollback", new PublishRequest { Revision = 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WaitForAsync(client, "/invoices/1", HttpStatusCode.NotFound)).ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/orders/1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
