// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatewright.DataPlane.Tests.Tls;

public sealed class KestrelSniTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-sni-").FullName;
    private WebApplication? _app;
    private int _port;

    public async Task InitializeAsync()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:Tls:HttpsPort"] = _port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Gateway:Tls:HttpPort"] = "0",
            ["Gateway:Tls:ReloadSeconds"] = "1",
            ["Urls"] = string.Empty,
        });
        builder.Logging.ClearProviders();
        builder.Services.AddGatewrightTls(builder.Configuration);
        builder.WebHost.UseGatewrightTls(builder.Configuration);
        _app = builder.Build();
        _app.MapGet("/", (HttpContext context) => "hello " + context.Request.Host.Host);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Directory.Delete(_directory, recursive: true);
    }

    private CertificateStore Store => _app!.Services.GetRequiredService<CertificateStore>();

    private static HostnameDefinition Host(string name, (string CertPath, string KeyPath, string Thumbprint) cert) => new()
    {
        Name = name,
        Certificate = new CertificateSource { File = new CertificateFiles(cert.CertPath, cert.KeyPath) },
    };

    private async Task<(string Body, string Thumbprint)> GetAsync(string host)
    {
        string? thumbprint = null;
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, _port, token);
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    // Test certificates are self-signed: the test asserts on the thumbprint instead of trusting a chain.
                    thumbprint = new X509Certificate2(certificate!).Thumbprint;
                    return thumbprint.Length > 0;
                },
            },
        };
        using var client = new HttpClient(handler);
        var body = await client.GetStringAsync($"https://{host}/");
        return (body, thumbprint!);
    }

    [Fact]
    public async Task Sni_SelectsTheCertificateOfEachHostname()
    {
        var api = TestCertificates.Write(_directory, "api", "api.example.com");
        var shop = TestCertificates.Write(_directory, "shop", "*.shop.example.com");
        var fallback = TestCertificates.Write(_directory, "default", "default.test");
        Store.Apply(new SecurityData { Hostnames = [Host("api.example.com", api), Host("*.shop.example.com", shop), Host("*", fallback)] });

        (await GetAsync("api.example.com")).Thumbprint.ShouldBe(api.Thumbprint);
        (await GetAsync("eu.shop.example.com")).Thumbprint.ShouldBe(shop.Thumbprint);
        var other = await GetAsync("whatever.test");
        other.Thumbprint.ShouldBe(fallback.Thumbprint);
        other.Body.ShouldBe("hello whatever.test");
    }

    [Fact]
    public async Task UnknownHostWithoutDefaultCertificate_FailsTheHandshake()
    {
        var api = TestCertificates.Write(_directory, "api", "api.example.com");
        Store.Apply(new SecurityData { Hostnames = [Host("api.example.com", api)] });

        await Should.ThrowAsync<HttpRequestException>(() => GetAsync("unknown.test"));
    }

    [Fact]
    public async Task CertificateRotation_IsPickedUpByNewConnectionsWithoutRestart()
    {
        var first = TestCertificates.Write(_directory, "site", "api.example.com");
        Store.Apply(new SecurityData { Hostnames = [Host("api.example.com", first)] });
        (await GetAsync("api.example.com")).Thumbprint.ShouldBe(first.Thumbprint);

        var second = TestCertificates.Write(_directory, "site", "api.example.com");

        // The reload service checks the files every second.
        string current = first.Thumbprint;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (current != second.Thumbprint && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            current = (await GetAsync("api.example.com")).Thumbprint;
        }

        current.ShouldBe(second.Thumbprint);
    }
}
