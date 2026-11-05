// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;

namespace Gatewright.IntegrationTests;

public sealed class ApiKeyPolicyTests
{
    private static readonly ApiKeyHasher Hasher = new(null);

    private static Task<TestGateway> StartAsync(string policyConfig = "{}") => TestGateway.StartAsync(url => new GatewayConfig
    {
        Products = [new ProductDefinition { Name = "gold" }, new ProductDefinition { Name = "silver" }],
        Subscriptions =
        [
            new SubscriptionDefinition { Id = "sub-gold", Product = "gold", Plan = "premium", KeyHashes = [Hasher.Hash("gold-primary"), Hasher.Hash("gold-secondary")] },
            new SubscriptionDefinition { Id = "sub-silver", Product = "silver", KeyHashes = [Hasher.Hash("silver-key")] },
            new SubscriptionDefinition { Id = "sub-off", Product = "gold", Active = false, KeyHashes = [Hasher.Hash("off-key")] },
        ],
        Apis =
        [
            TestGateway.Api(url, new PolicySet
            {
                Inbound =
                [
                    TestGateway.Policy("apiKey", policyConfig),
                    TestGateway.Policy("headers", """{"request":{"set":{"x-sub":"{subscription.id}","x-plan":"{subscription.plan}"}}}"""),
                ],
            }),
        ],
    });

    private static async Task<HttpResponseMessage> CallAsync(TestGateway gateway, string? key, string url = "/api/x")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (key is not null)
        {
            request.Headers.Add("x-api-key", key);
        }

        return await gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task ValidKey_ResolvesSubscription_AndKeyIsStrippedBeforeBackend()
    {
        await using var gateway = await StartAsync();

        var response = await CallAsync(gateway, "gold-secondary");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var headers = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("headers");
        headers.GetProperty("x-sub").GetString().ShouldBe("sub-gold");
        headers.GetProperty("x-plan").GetString().ShouldBe("premium");
        headers.TryGetProperty("x-api-key", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task MissingOrUnknownKey_Returns401()
    {
        await using var gateway = await StartAsync();

        (await CallAsync(gateway, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(gateway, "nope")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        gateway.BackendCalls.ShouldBe(0);
    }

    [Fact]
    public async Task InactiveSubscription_Returns403()
    {
        await using var gateway = await StartAsync();

        (await CallAsync(gateway, "off-key")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProductRestriction_RejectsOtherProducts()
    {
        await using var gateway = await StartAsync("""{"products":["gold"]}""");

        (await CallAsync(gateway, "gold-primary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "silver-key")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task QueryParameter_IsAcceptedAndStripped()
    {
        await using var gateway = await StartAsync("""{"queryParameter":"key"}""");

        var response = await CallAsync(gateway, null, "/api/x?key=gold-primary&q=1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("query").GetString().ShouldBe("?q=1");
    }

    [Fact]
    public async Task OptionalKey_PassesAnonymouslyButInvalidKeyIsStillRejected()
    {
        await using var gateway = await StartAsync("""{"required":false}""");

        (await CallAsync(gateway, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "nope")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Hasher_PepperChangesHash_AndHashIsStable()
    {
        new ApiKeyHasher("pepper-a").Hash("k").ShouldNotBe(new ApiKeyHasher("pepper-b").Hash("k"));
        new ApiKeyHasher("pepper-a").Hash("k").ShouldBe(new ApiKeyHasher("pepper-a").Hash("k"));
        Hasher.Hash("k").Length.ShouldBe(64);
    }

    [Fact]
    public async Task Config_DuplicateKeyHashAcrossSubscriptions_IsRejected()
    {
        var act = () => TestGateway.StartAsync(url => new GatewayConfig
        {
            Products = [new ProductDefinition { Name = "p" }],
            Subscriptions =
            [
                new SubscriptionDefinition { Id = "a", Product = "p", KeyHashes = ["abc"] },
                new SubscriptionDefinition { Id = "b", Product = "p", KeyHashes = ["ABC"] },
            ],
            Apis = [TestGateway.Api(url)],
        });

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("subscription.duplicate-key");
    }
}
