// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.IntegrationTests;

public sealed class ResiliencePolicyTests
{
    private static Task<TestGateway> StartAsync(string[][] backendPolicies, Action<IServiceCollection>? services = null) =>
        TestGateway.StartAsync(
            url => new GatewayConfig
            {
                Apis =
                [
                    TestGateway.Api(url, new PolicySet
                    {
                        Backend = [.. backendPolicies.Select(p => TestGateway.Policy(p[0], p.Length > 1 ? p[1] : null))],
                    }),
                ],
            },
            services);

    private static async Task<HttpResponseMessage> SendAsync(TestGateway gateway, string path, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        return await gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task Timeout_BackendTooSlow_Returns504()
    {
        await using var gateway = await StartAsync([["timeout", """{"seconds":0.2}"""]]);

        var response = await SendAsync(gateway, "/api/slow/3000");

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        (await response.Content.ReadAsStringAsync()).ShouldContain("backend_timeout");
    }

    [Fact]
    public async Task Timeout_ManyConcurrentHungRequests_AllFailFast_AndTheGatewayStaysResponsive()
    {
        // Chaos (14.5): a backend that hangs under load must cost the gateway one bounded timeout per
        // request, not exhaust it - requests after the storm are served at normal speed.
        await using var gateway = await StartAsync([["timeout", """{"seconds":0.3}"""]]);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var storm = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => SendAsync(gateway, "/api/slow/30000")));

        stopwatch.Stop();
        // 504 is the contract; a 502 is tolerated for the few connections the burst makes the (test) backend reset
        // instead of letting them time out. Either way: bounded, fast, and no hang.
        storm.ShouldAllBe(r => r.StatusCode == HttpStatusCode.GatewayTimeout || r.StatusCode == HttpStatusCode.BadGateway);
        storm.Count(r => r.StatusCode == HttpStatusCode.GatewayTimeout).ShouldBeGreaterThan(storm.Length / 2);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));

        var after = System.Diagnostics.Stopwatch.StartNew();
        (await SendAsync(gateway, "/api/slow/10")).StatusCode.ShouldBe(HttpStatusCode.OK);
        after.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Timeout_FastBackend_PassesThrough()
    {
        await using var gateway = await StartAsync([["timeout", """{"seconds":2}"""]]);

        var response = await SendAsync(gateway, "/api/slow/10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("slow-done");
    }

    [Fact]
    public async Task Retry_TransientConnectionFailures_AreRetriedUntilSuccess()
    {
        await using var gateway = await StartAsync([["retry", """{"maxAttempts":3,"backoffMilliseconds":5}"""]]);

        var response = await SendAsync(gateway, "/api/flaky/2?id=a");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("flaky-ok");
        gateway.BackendCalls.ShouldBe(3);
    }

    [Fact]
    public async Task Retry_AttemptsExhausted_ReturnsGatewayError()
    {
        await using var gateway = await StartAsync([["retry", """{"maxAttempts":2,"backoffMilliseconds":5}"""]]);

        var response = await SendAsync(gateway, "/api/flaky/5?id=b");

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        gateway.BackendCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Retry_NonIdempotentMethod_IsNotRetried()
    {
        await using var gateway = await StartAsync([["retry", """{"maxAttempts":3,"backoffMilliseconds":5}"""]]);

        var response = await SendAsync(gateway, "/api/flaky/1?id=c", HttpMethod.Post);

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        gateway.BackendCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RetryThenTimeout_EachAttemptHasItsOwnTimeout()
    {
        await using var gateway = await StartAsync(
        [
            ["retry", """{"maxAttempts":3,"backoffMilliseconds":5}"""],
            ["timeout", """{"seconds":0.2}"""],
        ]);

        var response = await SendAsync(gateway, "/api/slowfirst/2?id=d");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("slowfirst-ok");
        gateway.BackendCalls.ShouldBe(3);
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdAndRecoversAfterBreak()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await using var gateway = await StartAsync(
            [["circuitBreaker", """{"failureThreshold":2,"breakSeconds":30}"""]],
            services => services.AddSingleton<TimeProvider>(time));

        (await SendAsync(gateway, "/api/status/503")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await SendAsync(gateway, "/api/status/503")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var callsBeforeOpen = gateway.BackendCalls;

        var rejected = await SendAsync(gateway, "/api/ok");

        rejected.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await rejected.Content.ReadAsStringAsync()).ShouldContain("circuit_open");
        rejected.Headers.GetValues("Retry-After").Single().ShouldBe("30");
        gateway.BackendCalls.ShouldBe(callsBeforeOpen);

        time.Advance(TimeSpan.FromSeconds(31));

        (await SendAsync(gateway, "/api/ok")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(gateway, "/api/ok")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CircuitBreaker_FailedTrial_ReopensTheCircuit()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await using var gateway = await StartAsync(
            [["circuitBreaker", """{"failureThreshold":1,"breakSeconds":10}"""]],
            services => services.AddSingleton<TimeProvider>(time));
        await SendAsync(gateway, "/api/status/503");
        time.Advance(TimeSpan.FromSeconds(11));

        (await SendAsync(gateway, "/api/status/503")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable); // trial fails
        var next = await SendAsync(gateway, "/api/ok");

        (await next.Content.ReadAsStringAsync()).ShouldContain("circuit_open");
    }

    [Fact]
    public async Task Config_InvalidValues_AreRejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync([["timeout", """{"seconds":0}"""]]));
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync([["retry", """{"methods":["POST"]}"""]]));
    }
}
