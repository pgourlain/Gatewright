// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.DataPlane.Tests;

public sealed class ForwardedHeadersTests
{
    private static async Task<(string Ip, string Scheme)> CallAsync(string trusted, string peer, string forwardedFor, string proto = "https")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"{ForwardedHeadersExtensions.TrustedProxiesKey}:0"] = trusted });
        builder.Services.AddGatewrightForwardedHeaders(builder.Configuration);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            await next();
        });
        app.UseGatewrightForwardedHeaders();
        app.MapGet("/", (HttpContext context) => $"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", proto);

        var parts = (await client.SendAsync(request).ContinueWith(t => t.Result.Content.ReadAsStringAsync()).Unwrap()).Split('|');
        return (parts[0], parts[1]);
    }

    [Fact]
    public async Task TrustedProxy_ClientAddressAndSchemeAreRestored()
    {
        var (ip, scheme) = await CallAsync("10.0.0.0/8", "10.0.0.7", "203.0.113.9");

        ip.ShouldBe("203.0.113.9");
        scheme.ShouldBe("https");
    }

    [Fact]
    public async Task TrustedSingleAddress_IsSupported()
    {
        var (ip, _) = await CallAsync("10.0.0.7", "10.0.0.7", "203.0.113.9");

        ip.ShouldBe("203.0.113.9");
    }

    [Fact]
    public async Task UntrustedPeer_CannotSpoofTheClientAddressOrScheme()
    {
        var (ip, scheme) = await CallAsync("10.0.0.0/8", "198.51.100.1", "203.0.113.9");

        ip.ShouldBe("198.51.100.1");
        scheme.ShouldBe("http");
    }

    [Fact]
    public void NoTrustedProxiesConfigured_ForwardedHeadersAreDisabled()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddGatewrightForwardedHeaders(configuration).ShouldBeFalse();
    }
}
