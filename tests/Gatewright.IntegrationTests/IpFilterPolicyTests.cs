// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Core.Configuration;

namespace Gatewright.IntegrationTests;

public sealed class IpFilterPolicyTests
{
    private static Task<TestGateway> StartAsync(string config) => TestGateway.StartAsync(url => new GatewayConfig
    {
        Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("ipFilter", config)] })],
    });

    private static async Task<HttpStatusCode> CallAsync(TestGateway gateway, string remote, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/x");
        request.Headers.Add("x-test-remote-ip", remote);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return (await gateway.Client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Allow_ClientInRange_IsAccepted_OthersRejected()
    {
        await using var gateway = await StartAsync("""{"allow":["10.0.0.0/8"]}""");

        (await CallAsync(gateway, "10.1.2.3")).ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "192.168.1.1")).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deny_WinsOverAllow()
    {
        await using var gateway = await StartAsync("""{"allow":["10.0.0.0/8"],"deny":["10.9.9.9"]}""");

        (await CallAsync(gateway, "10.9.9.9")).ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(gateway, "10.9.9.8")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForwardedFor_FromUntrustedPeer_IsIgnored()
    {
        await using var gateway = await StartAsync("""{"allow":["203.0.113.0/24"]}""");

        // Spoofed header: the direct peer is not a trusted proxy, so 198.51.100.7 is what counts.
        (await CallAsync(gateway, "198.51.100.7", forwardedFor: "203.0.113.5")).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ForwardedFor_FromTrustedProxy_UsesRightmostUntrustedHop()
    {
        await using var gateway = await StartAsync("""{"allow":["203.0.113.0/24"],"trustedProxies":["10.0.0.0/8"]}""");

        (await CallAsync(gateway, "10.0.0.1", forwardedFor: "203.0.113.5, 10.0.0.2")).ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "10.0.0.1", forwardedFor: "203.0.113.5, 198.51.100.7, 10.0.0.2")).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Config_InvalidCidr_IsRejectedAtCompile()
    {
        var act = () => StartAsync("""{"allow":["not-an-ip"]}""");

        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Ipv4MappedAddress_MatchesIpv4Range()
    {
        await using var gateway = await StartAsync("""{"allow":["10.0.0.0/8"]}""");

        (await CallAsync(gateway, "::ffff:10.0.0.5")).ShouldBe(HttpStatusCode.OK);
    }
}
