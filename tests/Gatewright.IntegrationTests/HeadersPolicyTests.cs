// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Configuration;

namespace Gatewright.IntegrationTests;

public sealed class HeadersPolicyTests
{
    private static async Task<JsonElement> EchoAsync(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Headers_RequestAndResponseOperations_AreAppliedEndToEnd()
    {
        await using var gateway = await TestGateway.StartAsync(url => new GatewayConfig
        {
            Apis =
            [
                TestGateway.Api(url, new PolicySet
                {
                    Inbound = [TestGateway.Policy("headers", """{"request":{"set":{"x-api":"{api.name}","x-fixed":"1"},"append":{"x-list":"b"},"remove":["x-secret"]}}""")],
                    Outbound = [TestGateway.Policy("headers", """{"response":{"set":{"x-served-by":"gatewright"},"remove":["content-length-hint"]}}""")],
                }),
            ],
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/thing");
        request.Headers.Add("x-secret", "hunter2");
        request.Headers.Add("x-list", "a");

        var response = await gateway.Client.SendAsync(request);

        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Headers.GetValues("x-served-by").ShouldBe(["gatewright"]);
        var headers = (await EchoAsync(response)).GetProperty("headers");
        headers.GetProperty("x-api").GetString().ShouldBe("api");
        headers.GetProperty("x-fixed").GetString().ShouldBe("1");
        headers.GetProperty("x-list").GetString().ShouldBe("a, b");
        headers.TryGetProperty("x-secret", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Headers_UnknownProperty_IsRejectedByTheSchema()
    {
        var act = () => TestGateway.StartAsync(url => new GatewayConfig
        {
            Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("headers", """{"requst":{}}""")] })],
        });

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("headers");
    }
}
