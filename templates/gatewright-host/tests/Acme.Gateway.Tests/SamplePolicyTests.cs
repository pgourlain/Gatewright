using System.Net;
using Acme.Gateway;
using Gatewright.Core.Configuration;
using Gatewright.Testing;
using Xunit;

namespace Acme.Gateway.Tests;

public sealed class SamplePolicyTests
{
    private static Task<GatewayTestHost> StartAsync() =>
        GatewayTestHost.StartAsync(
            builder => builder.AddPolicy<SamplePolicy>(),
            backend => new GatewayConfig
            {
                Apis =
                [
                    GatewayTestHost.Api(backend, "orders", new PolicySet
                    {
                        Inbound = [GatewayTestHost.Policy("acme.requireHeader", """{"header":"x-tenant"}""")],
                    }),
                ],
            });

    [Fact]
    public async Task RequestWithTheHeader_ReachesTheBackend()
    {
        await using var host = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1");
        request.Headers.Add("x-tenant", "acme");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Backend.Requests);
    }

    [Fact]
    public async Task RequestWithoutTheHeader_IsRejectedBeforeTheBackend()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/orders/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Backend.Requests);
    }
}
