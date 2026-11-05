// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;

namespace Gatewright.IntegrationTests;

public sealed class CorsPolicyTests
{
    private const string Allowed = "https://app.example.com";

    private static Task<TestGateway> StartAsync(string config) => TestGateway.StartAsync(url => new GatewayConfig
    {
        Apis =
        [
            TestGateway.Api(url, new PolicySet
            {
                Inbound = [TestGateway.Policy("cors", config)],
                Outbound = [TestGateway.Policy("cors", config)],
            }),
        ],
    });

    private static HttpRequestMessage Request(HttpMethod method, string origin, string? requestMethod = null)
    {
        var request = new HttpRequestMessage(method, "/api/x");
        request.Headers.Add("Origin", origin);
        if (requestMethod is not null)
        {
            request.Headers.Add("Access-Control-Request-Method", requestMethod);
            request.Headers.Add("Access-Control-Request-Headers", "x-custom");
        }

        return request;
    }

    [Fact]
    public async Task Preflight_AllowedOrigin_IsAnsweredByGatewayWithoutCallingBackend()
    {
        await using var gateway = await StartAsync($$"""{"origins":["{{Allowed}}"],"headers":["x-custom"],"credentials":true,"maxAgeSeconds":30}""");

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Options, Allowed, "POST"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Allowed]);
        response.Headers.GetValues("Access-Control-Allow-Headers").ShouldBe(["x-custom"]);
        response.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
        response.Headers.GetValues("Access-Control-Max-Age").ShouldBe(["30"]);
        gateway.BackendCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Preflight_DisallowedOrigin_IsForbidden()
    {
        await using var gateway = await StartAsync($$"""{"origins":["{{Allowed}}"]}""");

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Options, "https://evil.example", "GET"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task SimpleRequest_AllowedOrigin_GetsCorsHeaders()
    {
        await using var gateway = await StartAsync($$"""{"origins":["{{Allowed}}"],"exposeHeaders":["x-total"]}""");

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Get, Allowed));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Allowed]);
        response.Headers.GetValues("Access-Control-Expose-Headers").ShouldBe(["x-total"]);
        response.Headers.GetValues("Vary").ShouldContain("Origin");
    }

    [Fact]
    public async Task SimpleRequest_DisallowedOrigin_GetsNoCorsHeaders()
    {
        await using var gateway = await StartAsync($$"""{"origins":["{{Allowed}}"]}""");

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Get, "https://evil.example"));

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task SimpleRequest_WildcardOrigin_ReturnsStar()
    {
        await using var gateway = await StartAsync("""{"origins":["*"]}""");

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Get, "https://anything.example"));

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
    }

    [Fact]
    public async Task Request_WithoutOrigin_IsUntouched()
    {
        await using var gateway = await StartAsync($$"""{"origins":["{{Allowed}}"]}""");

        var response = await gateway.Client.GetAsync("/api/x");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Config_WildcardWithCredentials_IsRejectedAtCompile()
    {
        var act = () => StartAsync("""{"origins":["*"],"credentials":true}""");

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("Credentials");
    }
}
