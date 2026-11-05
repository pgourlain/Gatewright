// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Acme.Gateway;
using Gatewright.Core.Configuration;
using Gatewright.Testing;

namespace Gatewright.Samples.Tests;

// The policy telemetry (meter) is static, so tests that reject orders must not run in parallel.
[Collection("fraud-telemetry")]
public sealed class FraudCheckPolicyTests
{
    private static Task<GatewayTestHost> StartAsync(string policyConfig = """{"maxAmount":1000,"blockedCountries":["XX","YY"]}""") =>
        GatewayTestHost.StartAsync(
            builder => builder.AddPolicy<FraudCheckPolicy>(),
            backend => new GatewayConfig
            {
                Apis = [GatewayTestHost.Api(backend, "orders", new PolicySet { Inbound = [GatewayTestHost.Policy("acme.fraudCheck", policyConfig)] })],
            });

    private static async Task<HttpResponseMessage> PostOrderAsync(GatewayTestHost host, string? amount = null, string? country = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders/create") { Content = new StringContent("{}") };
        if (amount is not null)
        {
            request.Headers.Add("x-order-amount", amount);
        }

        if (country is not null)
        {
            request.Headers.Add("x-country", country);
        }

        return await host.Client.SendAsync(request);
    }

    [Fact]
    public async Task NormalOrder_ReachesTheBackend()
    {
        await using var host = await StartAsync();

        var response = await PostOrderAsync(host, amount: "250.50", country: "FR");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Backend.Requests.ShouldHaveSingleItem().Path.ShouldBe("/create");
    }

    [Fact]
    public async Task OrderAboveTheLimit_IsRejectedBeforeTheBackend()
    {
        await using var host = await StartAsync();

        var response = await PostOrderAsync(host, amount: "1000.01");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("fraud_amount_too_high");
        host.Backend.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("yy")]
    public async Task BlockedCountry_IsRejected(string country)
    {
        await using var host = await StartAsync();

        (await PostOrderAsync(host, country: country)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        host.Backend.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReconfiguringThePolicy_TakesEffectWithoutRestart()
    {
        await using var host = await StartAsync();
        (await PostOrderAsync(host, amount: "5000")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await host.UpdateConfigAsync(new GatewayConfig
        {
            Apis = [GatewayTestHost.Api(host.Backend, "orders", new PolicySet { Inbound = [GatewayTestHost.Policy("acme.fraudCheck", """{"maxAmount":10000}""")] })],
        });

        (await PostOrderAsync(host, amount: "5000")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidPolicyConfiguration_IsRefusedWithAClearError()
    {
        var act = () => StartAsync("""{"blockedCountries":["XX"]}""");

        var ex = await Should.ThrowAsync<InvalidOperationException>(act);
        ex.Message.ShouldContain("did not apply");
    }

    [Fact]
    public async Task CustomPolicy_AppearsInThePolicyCatalogWithItsGeneratedSchema()
    {
        await using var host = await StartAsync();
        var registry = host.Services.GetService(typeof(Gatewright.Core.Policies.PolicyRegistry)) as Gatewright.Core.Policies.PolicyRegistry;

        var descriptor = registry!.Describe().Single(d => d.Type == "acme.fraudCheck");

        descriptor.Schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["maxAmount"]);
        descriptor.Schema.GetProperty("properties").GetProperty("maxAmount").GetProperty("description").GetString().ShouldBe("Orders above this amount are rejected.");
        descriptor.Schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        registry.Types.ShouldContain("rateLimit");
    }
}
