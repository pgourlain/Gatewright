// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Gatewright.DataPlane;
using Gatewright.DataPlane.Tls;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.IntegrationTests;

public sealed class DistributionEndToEndTests : IAsyncLifetime
{
    private const string Token = "distribution-secret";

    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-dist-").FullName;
    private readonly List<IAsyncDisposable> _disposables = [];
    private WebApplication? _backend;

    private sealed class InMemoryChannelProvider(Func<HttpMessageHandler> handler) : IGrpcChannelProvider
    {
        public GrpcChannel Create(Uri endpoint) =>
            GrpcChannel.ForAddress("http://control-plane.test", new GrpcChannelOptions { HttpHandler = handler(), DisposeHttpClient = true });
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _backend = builder.Build();
        _backend.MapGet("/{**path}", (HttpContext ctx) => "backend:" + ctx.Request.Path);
        await _backend.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
        {
            await disposable.DisposeAsync();
        }

        if (_backend is not null)
        {
            await _backend.DisposeAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeSecretFetcher : ISecretFetcher
    {
        public Dictionary<string, CertificateMaterial> Materials { get; } = new(StringComparer.Ordinal);

        public ValueTask<CertificateMaterial?> FetchAsync(string reference, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Materials.GetValueOrDefault(reference));
    }

    private static (string CertPem, string KeyPem, string Thumbprint) SelfSignedCertificate(string dnsName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        return (certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem(), certificate.Thumbprint);
    }

    private WebApplicationFactory<Program> StartControlPlane(ISecretFetcher? secretFetcher = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Gateway:Mode", "ControlPlane");
            b.UseSetting("ControlPlane:ConnectionString", $"Data Source={Path.Combine(_directory, "cp.db")}");
            b.UseSetting("ControlPlane:Auth:DevelopmentBypass", "true");
            b.UseSetting("Distribution:Tokens:0", Token);
            b.UseSetting("Distribution:PollMilliseconds", "50");
            b.UseSetting("Distribution:DebounceMilliseconds", "50");
            b.UseSetting("Distribution:HeartbeatSeconds", "1");
            b.UseSetting("Kubernetes:Secrets:PollSeconds", "1");
            if (secretFetcher is not null)
            {
                b.ConfigureTestServices(services => services.AddSingleton(secretFetcher));
            }
        });
        _disposables.Add(factory);
        _ = factory.Server;
        return factory;
    }

    private WebApplicationFactory<Program> StartDataPlane(WebApplicationFactory<Program> controlPlane, string nodeId, string token = Token)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:Mode", "DataPlane");
            b.UseSetting("Gateway:Environment", "dev");
            b.UseSetting("Distribution:Endpoint", "http://control-plane.test");
            b.UseSetting("Distribution:Token", token);
            b.UseSetting("Distribution:NodeId", nodeId);
            b.UseSetting("Distribution:HeartbeatSeconds", "1");
            b.UseSetting("Distribution:MaxReconnectSeconds", "1");
            b.ConfigureTestServices(services => services.AddSingleton<IGrpcChannelProvider>(new InMemoryChannelProvider(() => controlPlane.Server.CreateHandler())));
        });
        _disposables.Add(factory);
        _ = factory.Server;
        return factory;
    }

    private ApiRequest Api(string name) => new()
    {
        Name = name,
        BasePath = "/" + name,
        Backend = new BackendDefinition { Destinations = [_backend!.Urls.First()] },
    };

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return await condition();
    }

    private static async Task<bool> ServesAsync(HttpClient client, string path) => (await client.GetAsync(path)).StatusCode == HttpStatusCode.OK;

    [Fact]
    public async Task OneControlPlaneThreeDataPlanes_PublishIsAppliedEverywhereInUnderFiveSeconds_AndTrafficSurvivesTheControlPlane()
    {
        var controlPlane = StartControlPlane();
        using var admin = controlPlane.CreateClient();
        var dataPlanes = Enumerable.Range(1, 3).Select(i => StartDataPlane(controlPlane, $"dp-{i}")).ToArray();
        var clients = dataPlanes.Select(d => d.CreateClient()).ToArray();

        (await admin.PostAsJsonAsync("/admin/v1/apis", Api("orders"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var stopwatch = Stopwatch.StartNew();
        (await admin.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var client in clients)
        {
            (await WaitUntilAsync(() => ServesAsync(client, "/orders/1"), TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        (await admin.PostAsJsonAsync("/admin/v1/apis", Api("invoices"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        stopwatch.Restart();
        (await admin.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest())).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var client in clients)
        {
            (await WaitUntilAsync(() => ServesAsync(client, "/invoices/1"), TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        // The control plane knows which version each node applied.
        (await WaitUntilAsync(
            async () =>
            {
                var nodes = JsonElement.Parse(await admin.GetStringAsync("/admin/v1/nodes?environment=dev"));
                return nodes.GetArrayLength() == 3 && nodes.EnumerateArray().All(n => n.GetProperty("inSync").GetBoolean() && n.GetProperty("appliedVersion").GetInt64() == 2);
            },
            TimeSpan.FromSeconds(5))).ShouldBeTrue();

        // Losing the control plane never stops traffic.
        await controlPlane.DisposeAsync();
        await Task.Delay(300);
        foreach (var client in clients)
        {
            (await client.GetAsync("/orders/1")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task DataPlane_WithTheWrongToken_NeverReceivesAConfiguration()
    {
        var controlPlane = StartControlPlane();
        using var admin = controlPlane.CreateClient();
        await admin.PostAsJsonAsync("/admin/v1/apis", Api("orders"));
        await admin.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest());
        var dataPlane = StartDataPlane(controlPlane, "intruder", token: "wrong");
        using var client = dataPlane.CreateClient();

        await Task.Delay(1500);

        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/orders/1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DataPlane_StartedBeforeAnyPublication_ReceivesTheFirstOneWhenItHappens()
    {
        var controlPlane = StartControlPlane();
        using var admin = controlPlane.CreateClient();
        var dataPlane = StartDataPlane(controlPlane, "early");
        using var client = dataPlane.CreateClient();
        await Task.Delay(500);
        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        await admin.PostAsJsonAsync("/admin/v1/apis", Api("orders"));
        await admin.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest());

        (await WaitUntilAsync(() => ServesAsync(client, "/orders/1"), TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await client.GetAsync("/readyz")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HostnameWithASecretSource_PushesTheCertificateToTheDataPlane_OutsideAnyPublication()
    {
        var (certPem, keyPem, thumbprint) = SelfSignedCertificate("api.example.com");
        var fetcher = new FakeSecretFetcher();
        fetcher.Materials["ns/tls"] = new CertificateMaterial(certPem, keyPem);
        var controlPlane = StartControlPlane(fetcher);
        using var admin = controlPlane.CreateClient();
        var dataPlane = StartDataPlane(controlPlane, "dp-tls");

        (await admin.PutAsJsonAsync("/admin/v1/hostnames/api.example.com", new HostnameRequest { Certificate = new CertificateSource { Secret = "ns/tls" } }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await admin.PostAsJsonAsync("/admin/v1/environments/dev/publications", new PublishRequest());

        X509Certificate2? Selected() => dataPlane.Services.GetRequiredService<CertificateStore>().Select("api.example.com");
        (await WaitUntilAsync(() => Task.FromResult(Selected() is not null), TimeSpan.FromSeconds(10))).ShouldBeTrue();
        Selected()!.Thumbprint.ShouldBe(thumbprint);
    }
}
