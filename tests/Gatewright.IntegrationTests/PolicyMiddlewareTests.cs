// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Gatewright.DataPlane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.IntegrationTests;

public sealed class PolicyMiddlewareTests : IAsyncLifetime
{
    private WebApplication? _backend;
    private WebApplication? _gateway;
    private HttpClient? _client;

    public async Task InitializeAsync() => await InitializeCoreAsync();

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_gateway is not null)
        {
            await _gateway.DisposeAsync();
        }

        if (_backend is not null)
        {
            await _backend.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_NoRejectingPolicy_ProxiesAndOutboundPolicyAddsHeader()
    {
        var response = await _client!.GetAsync("/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("backend:/42");
        response.Headers.GetValues("x-gateway").ShouldBe(["gatewright"]);
    }

    [Fact]
    public async Task Get_InboundPolicyRejects_ReturnsProblemWithoutCallingBackend()
    {
        var response = await _client!.GetAsync("/orders/secret");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        body.GetProperty("type").GetString().ShouldBe("rejected");
        body.GetProperty("status").GetInt32().ShouldBe(403);
    }

    [Fact]
    public async Task Get_UnknownPath_Returns404()
    {
        (await _client!.GetAsync("/nothing")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task InitializeCoreAsync()
    {
        var backendBuilder = WebApplication.CreateSlimBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        _backend = backendBuilder.Build();
        _backend.MapGet("/{**path}", (HttpContext ctx) => "backend:" + ctx.Request.Path);
        await _backend.StartAsync();
        var backendAddress = _backend.Urls.First();

        var config = new GatewayConfig
        {
            Version = 1,
            Apis =
            [
                new ApiDefinition
                {
                    Name = "orders",
                    BasePath = "/orders",
                    Backend = new BackendDefinition { Destinations = [backendAddress] },
                    Policies = new PolicySet
                    {
                        Outbound = [new PolicyReference { Type = "addHeader", Config = JsonElement.Parse("""{"name":"x-gateway","value":"gatewright"}""") }],
                    },
                    Operations =
                    [
                        new OperationDefinition { Name = "get", Method = "GET", Path = "/{id}" },
                        new OperationDefinition
                        {
                            Name = "secret",
                            Method = "GET",
                            Path = "/secret",
                            Policies = new PolicySet { Inbound = [new PolicyReference { Type = "reject", Config = JsonElement.Parse("""{"status":403}""") }] },
                        },
                    ],
                },
            ],
        };

        var gatewayBuilder = WebApplication.CreateBuilder();
        gatewayBuilder.WebHost.UseTestServer();
        gatewayBuilder.Services.AddGatewrightDataPlane();
        _gateway = gatewayBuilder.Build();
        _gateway.MapGatewrightDataPlane();

        var registry = new PolicyRegistry([new RejectPolicy(), new AddHeaderPolicy()]);
        var translator = new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, _gateway.Services));
        var translation = translator.Translate(config);
        translation.IsSuccess.ShouldBeTrue(string.Join("; ", translation.Errors));
        await _gateway.StartAsync();
        (await _gateway.Services.GetRequiredService<SnapshotApplier>().ApplyAsync(translation.Snapshot!)).ShouldBeTrue();

        _client = _gateway.GetTestClient();
    }
}
