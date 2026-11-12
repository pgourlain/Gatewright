// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gatewright.Core.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gatewright.IntegrationTests;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-health-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private WebApplicationFactory<Program> Factory(string? file, string? token = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:Mode", "All");
            b.UseSetting("Gateway:ConfigFile", file ?? Path.Combine(_directory, "missing.json"));
            b.UseSetting("Gateway:ConfigPollInterval", "00:00:00.100");
            if (token is not null)
            {
                b.UseSetting("Gateway:DiagnosticsToken", token);
            }
        });

    private string ValidConfig(long version)
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, GatewayConfigSerializer.Serialize(new GatewayConfig { Version = version }));
        return path;
    }

    private static async Task<HttpStatusCode> WaitForReadyAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        HttpStatusCode status;
        do
        {
            status = (await client.GetAsync("/readyz")).StatusCode;
            if (status == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        return status;
    }

    [Fact]
    public async Task Healthz_Always_ReturnsOk()
    {
        await using var factory = Factory(null);
        using var client = factory.CreateClient();

        (await client.GetAsync("/healthz")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readyz_BeforeFirstSnapshot_ReturnsServiceUnavailable()
    {
        await using var factory = Factory(null);
        using var client = factory.CreateClient();

        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Readyz_AfterSnapshotApplied_ReturnsOk()
    {
        await using var factory = Factory(ValidConfig(1));
        using var client = factory.CreateClient();

        (await WaitForReadyAsync(client)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GwConfig_NoTokenConfigured_IsNotFound()
    {
        await using var factory = Factory(ValidConfig(1));
        using var client = factory.CreateClient();

        (await client.GetAsync("/gw/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GwConfig_WrongOrMissingToken_IsUnauthorized()
    {
        await using var factory = Factory(ValidConfig(1), token: "s3cret");
        using var client = factory.CreateClient();

        (await client.GetAsync("/gw/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nope");
        (await client.GetAsync("/gw/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GwConfig_ValidToken_ReturnsVersionAndHash()
    {
        await using var factory = Factory(ValidConfig(7), token: "s3cret");
        using var client = factory.CreateClient();
        await WaitForReadyAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cret");

        var response = await client.GetAsync("/gw/config");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        json.GetProperty("version").GetInt64().ShouldBe(7);
        json.GetProperty("hash").GetString()!.Length.ShouldBe(64);
    }

    [Fact]
    public async Task CacheEvict_RequiresTokenAndTag()
    {
        await using var factory = Factory(ValidConfig(1), token: "s3cret");
        using var client = factory.CreateClient();

        (await client.PostAsync("/gw/cache/evict?tag=x", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cret");
        (await client.PostAsync("/gw/cache/evict", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync("/gw/cache/evict?tag=api:orders", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PreStop_Unauthenticated_MarksDrainingAndWaitsOutTheDelay()
    {
        // No shell in the chiseled image to run an exec preStop hook, so the chart points
        // Kubernetes' preStop at this httpGet target instead (14.2); the kubelet calls it by pod IP
        // with no credentials, so unlike /gw/config this one must work without a token.
        await using var factory = Factory(ValidConfig(1), token: "s3cret");
        using var client = factory.CreateClient();
        await WaitForReadyAsync(client);

        (await client.GetAsync("/gw/lifecycle/prestop")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task PolicyCatalog_ListsBuiltInPoliciesWithSchemas()
    {
        await using var factory = Factory(ValidConfig(1), token: "s3cret");
        using var client = factory.CreateClient();

        (await client.GetAsync("/gw/policies")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cret");
        var response = await client.GetAsync("/gw/policies");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var catalog = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        var types = catalog.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToArray();
        types.ShouldBe(
            ["apiKey", "cache", "circuitBreaker", "cors", "headers", "ipFilter", "jwt", "mockResponse", "quota", "rateLimit", "retry", "rewrite", "timeout"],
            ignoreOrder: true);
        var rateLimit = catalog.EnumerateArray().Single(e => e.GetProperty("type").GetString() == "rateLimit");
        rateLimit.GetProperty("schema").GetProperty("required")[0].GetString().ShouldBe("limit");
        rateLimit.GetProperty("phases")[0].GetString().ShouldBe("Inbound");
    }
}
