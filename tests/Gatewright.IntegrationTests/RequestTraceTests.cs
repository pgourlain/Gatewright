// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Configuration;
using Gatewright.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.IntegrationTests;

public sealed class RequestTraceTests
{
    private static Task<TestGateway> StartAsync() => TestGateway.StartAsync(
        url => new GatewayConfig
        {
            Apis =
            [
                TestGateway.Api(url, new PolicySet
                {
                    Inbound = [TestGateway.Policy("headers", """{"request":{"set":{"x-a":"1"}}}"""), TestGateway.Policy("rateLimit", """{"limit":5,"partitionBy":"global"}""")],
                    Outbound = [TestGateway.Policy("headers", """{"response":{"set":{"x-b":"2"}}}""")],
                }),
            ],
        },
        services => services.Configure<GatewayOptions>(o => o.DiagnosticsToken = "trace-token"));

    private static async Task<HttpResponseMessage> CallAsync(TestGateway gateway, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/x");
        if (token is not null)
        {
            request.Headers.Add("x-gatewright-trace", token);
        }

        return await gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task WithTheDiagnosticsToken_ResponseCarriesThePerPolicyTrace()
    {
        await using var gateway = await StartAsync();

        var response = await CallAsync(gateway, "trace-token");

        var raw = response.Headers.GetValues("x-gatewright-trace-result").Single();
        var entries = JsonElement.Parse(Convert.FromBase64String(raw)).EnumerateArray().ToList();
        entries.Select(e => $"{e.GetProperty("phase").GetString()}:{e.GetProperty("policy").GetString()}:{e.GetProperty("result").GetString()}")
            .ShouldBe(["Inbound:headers:Continue", "Inbound:rateLimit:Continue", "Outbound:headers:Continue"]);
        entries[0].GetProperty("durationMs").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task WithoutOrWithAWrongToken_NoTraceIsReturned()
    {
        await using var gateway = await StartAsync();

        (await CallAsync(gateway, null)).Headers.Contains("x-gatewright-trace-result").ShouldBeFalse();
        (await CallAsync(gateway, "nope")).Headers.Contains("x-gatewright-trace-result").ShouldBeFalse();
    }

    [Fact]
    public async Task RejectedRequests_StillReportWhichPolicyRejected()
    {
        await using var gateway = await StartAsync();
        for (var i = 0; i < 5; i++)
        {
            await CallAsync(gateway, null);
        }

        var response = await CallAsync(gateway, "trace-token");

        ((int)response.StatusCode).ShouldBe(429);
        var raw = response.Headers.GetValues("x-gatewright-trace-result").Single();
        JsonElement.Parse(Convert.FromBase64String(raw)).EnumerateArray().Last().GetProperty("result").GetString().ShouldBe("Reject");
    }
}
