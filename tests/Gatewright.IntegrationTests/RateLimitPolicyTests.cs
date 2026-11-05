// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;

namespace Gatewright.IntegrationTests;

public sealed class RateLimitPolicyTests
{
    private static readonly ApiKeyHasher Hasher = new(null);

    private static Task<TestGateway> StartAsync(string rateLimit, bool withApiKey = false) => TestGateway.StartAsync(url => new GatewayConfig
    {
        Products = [new ProductDefinition { Name = "p" }],
        Subscriptions =
        [
            new SubscriptionDefinition { Id = "s1", Product = "p", KeyHashes = [Hasher.Hash("k1")] },
            new SubscriptionDefinition { Id = "s2", Product = "p", KeyHashes = [Hasher.Hash("k2")] },
        ],
        Apis =
        [
            TestGateway.Api(url, new PolicySet
            {
                Inbound = withApiKey
                    ? [TestGateway.Policy("apiKey"), TestGateway.Policy("rateLimit", rateLimit)]
                    : [TestGateway.Policy("rateLimit", rateLimit)],
            }),
        ],
    });

    private static async Task<HttpResponseMessage> CallAsync(TestGateway gateway, string? key = null, string path = "/api/x")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key is not null)
        {
            request.Headers.Add("x-api-key", key);
        }

        return await gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task Exceeding_Limit_Returns429WithRetryAfterAndRateLimitHeaders()
    {
        await using var gateway = await StartAsync("""{"limit":3,"periodSeconds":60,"partitionBy":"global"}""");

        var first = await CallAsync(gateway);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.GetValues("RateLimit-Limit").ShouldBe(["3"]);
        first.Headers.GetValues("RateLimit-Remaining").ShouldBe(["2"]);
        await CallAsync(gateway);
        await CallAsync(gateway);

        var rejected = await CallAsync(gateway);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(rejected.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
        rejected.Headers.GetValues("RateLimit-Remaining").ShouldBe(["0"]);
        gateway.BackendCalls.ShouldBe(3);
    }

    [Fact]
    public async Task PartitionBySubscription_GivesEachSubscriptionItsOwnBudget()
    {
        await using var gateway = await StartAsync("""{"limit":1,"periodSeconds":600,"partitionBy":"subscription"}""", withApiKey: true);

        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await CallAsync(gateway, "k2")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PartitionByHeader_SeparatesValues()
    {
        await using var gateway = await StartAsync("""{"limit":1,"periodSeconds":600,"partitionBy":"header:x-tenant"}""");

        async Task<HttpStatusCode> Call(string tenant)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/x");
            request.Headers.Add("x-tenant", tenant);
            return (await gateway.Client.SendAsync(request)).StatusCode;
        }

        (await Call("a")).ShouldBe(HttpStatusCode.OK);
        (await Call("a")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Call("b")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FixedWindowAlgorithm_IsSupported()
    {
        await using var gateway = await StartAsync("""{"algorithm":"fixedWindow","limit":2,"periodSeconds":600,"partitionBy":"global"}""");

        (await CallAsync(gateway)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SharedName_CountsAcrossOperations()
    {
        await using var gateway = await TestGateway.StartAsync(url => new GatewayConfig
        {
            GlobalPolicies = new PolicySet
            {
                Inbound = [TestGateway.Policy("rateLimit", """{"name":"all","limit":2,"periodSeconds":600,"partitionBy":"global"}""")],
            },
            Apis =
            [
                TestGateway.Api(
                    url,
                    null,
                    "api",
                    new OperationDefinition { Name = "a", Method = "GET", Path = "/a" },
                    new OperationDefinition { Name = "b", Method = "GET", Path = "/b" }),
            ],
        });

        (await CallAsync(gateway, path: "/api/a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, path: "/api/b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, path: "/api/a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Config_InvalidPartitionOrLimit_IsRejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync("""{"limit":0}"""));
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync("""{"limit":1,"partitionBy":"planet"}"""));
    }
}
