// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.IntegrationTests;

public sealed class QuotaPolicyTests
{
    private static readonly ApiKeyHasher Hasher = new(null);

    private static Task<TestGateway> StartAsync(FakeTimeProvider time, string quota) => TestGateway.StartAsync(
        url => new GatewayConfig
        {
            Products = [new ProductDefinition { Name = "p" }],
            Subscriptions =
            [
                new SubscriptionDefinition { Id = "s1", Product = "p", KeyHashes = [Hasher.Hash("k1")] },
                new SubscriptionDefinition { Id = "s2", Product = "p", KeyHashes = [Hasher.Hash("k2")] },
            ],
            Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("apiKey"), TestGateway.Policy("quota", quota)] })],
        },
        services => services.AddSingleton<TimeProvider>(time));

    private static async Task<HttpResponseMessage> CallAsync(TestGateway gateway, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/x");
        request.Headers.Add("x-api-key", key);
        return await gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task Quota_ExceededForSubscription_Returns429UntilNextPeriod()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-10T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await using var gateway = await StartAsync(time, """{"limit":2,"period":"day"}""");

        var first = await CallAsync(gateway, "k1");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.GetValues("X-Quota-Remaining").ShouldBe(["1"]);
        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejected = await CallAsync(gateway, "k1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.GetValues("Retry-After").Single().ShouldBe("50400"); // 14 hours until midnight UTC
        (await CallAsync(gateway, "k2")).StatusCode.ShouldBe(HttpStatusCode.OK);

        time.Advance(TimeSpan.FromHours(15));

        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Quota_MonthlyPeriod_ResetsOnFirstOfMonth()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-31T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await using var gateway = await StartAsync(time, """{"limit":1,"period":"month"}""");

        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        time.Advance(TimeSpan.FromHours(2));

        (await CallAsync(gateway, "k1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Config_InvalidPeriod_IsRejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(new FakeTimeProvider(), """{"limit":1,"period":"year"}"""));
    }
}
