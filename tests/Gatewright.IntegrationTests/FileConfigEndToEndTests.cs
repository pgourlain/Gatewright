// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gatewright.IntegrationTests;

public sealed class FileConfigEndToEndTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-e2e-").FullName;
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

        Directory.Delete(_directory, recursive: true);
    }

    private string WriteConfig(long version, params string[] apiNames)
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new GatewayConfig
        {
            Version = version,
            Apis =
            [
                .. apiNames.Select(name => new ApiDefinition
                {
                    Name = name,
                    BasePath = "/" + name,
                    Backend = new BackendDefinition { Destinations = [_backend!.Urls.First()] },
                }),
            ],
        };
        File.WriteAllText(path + ".tmp", GatewayConfigSerializer.Serialize(config));
        File.Move(path + ".tmp", path, overwrite: true);
        return path;
    }

    private static async Task<HttpStatusCode> WaitForStatusAsync(HttpClient client, string url, HttpStatusCode expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        HttpStatusCode last;
        do
        {
            last = (await client.GetAsync(url)).StatusCode;
            if (last == expected)
            {
                return last;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        return last;
    }

    [Fact]
    public async Task Host_ConfigFileEdited_AppliesNewRoutesWithoutRestart()
    {
        var path = WriteConfig(1, "orders");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:Mode", "All");
            b.UseSetting("Gateway:ConfigFile", path);
            b.UseSetting("Gateway:ConfigPollInterval", "00:00:00.100");
        });
        using var client = factory.CreateClient();

        (await WaitForStatusAsync(client, "/orders/1", HttpStatusCode.OK, TimeSpan.FromSeconds(5))).ShouldBe(HttpStatusCode.OK);
        (await client.GetStringAsync("/orders/1")).ShouldBe("backend:/1");
        (await client.GetAsync("/invoices/1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        WriteConfig(2, "orders", "invoices");

        (await WaitForStatusAsync(client, "/invoices/1", HttpStatusCode.OK, TimeSpan.FromSeconds(5))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Host_ConfigFileBecomesInvalid_KeepsServingPreviousConfig()
    {
        var path = WriteConfig(1, "orders");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:Mode", "All");
            b.UseSetting("Gateway:ConfigFile", path);
            b.UseSetting("Gateway:ConfigPollInterval", "00:00:00.100");
        });
        using var client = factory.CreateClient();
        await WaitForStatusAsync(client, "/orders/1", HttpStatusCode.OK, TimeSpan.FromSeconds(5));

        await File.WriteAllTextAsync(path, """{"version":2,"apis":[{"name":"x","basePath":"nope","backend":{}}]}""");
        await Task.Delay(500);

        (await client.GetAsync("/orders/1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
