// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Policies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Sdk.Tests;

public sealed class GatewayHostTests
{
    [GatewayPolicy("test.noop")]
    private sealed class NoopPolicy : GatewayPolicy<NoopPolicy.Config>
    {
        public sealed record Config;

        protected override Gatewright.Abstractions.IPolicyInstance Create(Config config, Gatewright.Abstractions.PolicyCompilationContext context) =>
            PolicyInstance.From(_ => Gatewright.Abstractions.PolicyResult.Continue);
    }

    private static GatewayHostBuilder Builder() =>
        GatewayHost.CreateBuilder(["--Gateway:Mode=DataPlane", "--Distribution:Endpoint=http://localhost:1", "--Distribution:Token=t", "--urls=http://127.0.0.1:0"]);

    [Fact]
    public async Task AddPolicy_RegistersTheCustomPolicyNextToTheBuiltInOnes()
    {
        var builder = Builder().AddPolicy<NoopPolicy>();

        await using var app = builder.Build();

        var registry = app.Services.GetRequiredService<PolicyRegistry>();
        registry.Types.ShouldContain("test.noop");
        registry.Types.ShouldContain("jwt");
        registry.Types.ShouldContain("rateLimit");
    }

    [Fact]
    public async Task AddPolicy_Instance_IsSupported()
    {
        var builder = Builder().AddPolicy(new NoopPolicy());

        await using var app = builder.Build();

        app.Services.GetRequiredService<PolicyRegistry>().Types.ShouldContain("test.noop");
    }

    [Fact]
    public async Task Build_MapsTheOperationalEndpoints()
    {
        await using var app = Builder().Build();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.App.Urls.First()) };

            (await client.GetAsync("/healthz")).IsSuccessStatusCode.ShouldBeTrue();
            ((int)(await client.GetAsync("/readyz")).StatusCode).ShouldBe(503);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void Builder_ExposesTheUnderlyingAspNetCoreBuilder()
    {
        var builder = Builder();

        builder.Builder.ShouldBeOfType<WebApplicationBuilder>();
        builder.Services.ShouldBeSameAs(builder.Builder.Services);
    }

    [Fact]
    public async Task Build_OperationalEndpoints_CarrySecurityHeadersAndDoNotLeakTheServer()
    {
        await using var app = Builder().Build();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.App.Urls.First()) };

            using var response = await client.GetAsync("/healthz");

            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            response.Headers.Contains("Server").ShouldBeFalse();
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Build_RequestBodyAboveTheConfiguredLimit_IsRejectedWith413()
    {
        var builder = GatewayHost.CreateBuilder(
            ["--Gateway:Mode=DataPlane", "--Distribution:Endpoint=http://localhost:1", "--Distribution:Token=t", "--urls=http://127.0.0.1:0", "--Gateway:Limits:MaxRequestBodySize=10"]);
        await using var app = builder.Build();
        app.App.MapPost("/echo", async (HttpContext c) =>
        {
            using var reader = new StreamReader(c.Request.Body);
            return await reader.ReadToEndAsync();
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.App.Urls.First()) };

            using var response = await client.PostAsync("/echo", new StringContent(new string('x', 100)));

            ((int)response.StatusCode).ShouldBe(413);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void Build_InvalidRequestLimit_FailsAtStartup()
    {
        var builder = GatewayHost.CreateBuilder(
            ["--Gateway:Mode=DataPlane", "--Distribution:Endpoint=http://localhost:1", "--Distribution:Token=t", "--urls=http://127.0.0.1:0", "--Gateway:Limits:MaxRequestHeaderCount=0"]);

        Should.Throw<InvalidOperationException>(async () =>
        {
            await using var app = builder.Build();
            await app.StartAsync();
        });
    }
}
