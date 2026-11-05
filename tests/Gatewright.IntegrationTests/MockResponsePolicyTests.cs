// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;

namespace Gatewright.IntegrationTests;

public sealed class MockResponsePolicyTests
{
    private static Task<TestGateway> StartAsync(string mock) => TestGateway.StartAsync(url => new GatewayConfig
    {
        Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("mockResponse", mock)] })],
    });

    [Fact]
    public async Task Mock_ReturnsConfiguredResponseWithoutCallingBackend()
    {
        await using var gateway = await StartAsync("""{"status":201,"headers":{"x-mock":"yes"},"body":"{\"id\":1}"}""");

        var response = await gateway.Client.GetAsync("/api/anything");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.GetValues("x-mock").ShouldBe(["yes"]);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync()).ShouldBe("{\"id\":1}");
        gateway.BackendCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Mock_NoBody_ReturnsEmptyResponseWithStatus()
    {
        await using var gateway = await StartAsync("""{"status":204}""");

        var response = await gateway.Client.GetAsync("/api/x");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mock_CustomContentType_IsHonored()
    {
        await using var gateway = await StartAsync("""{"contentType":"text/plain","body":"hello"}""");

        var response = await gateway.Client.GetAsync("/api/x");

        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await response.Content.ReadAsStringAsync()).ShouldBe("hello");
    }

    [Fact]
    public async Task Config_InvalidStatus_IsRejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync("""{"status":42}"""));
    }
}
