// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Distribution;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.ControlPlane.Tests.Distribution;

public sealed class TlsSecretsHubTests : IDisposable
{
    private sealed class FakeFetcher : ISecretFetcher
    {
        public Dictionary<string, CertificateMaterial> Materials { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

        public ValueTask<CertificateMaterial?> FetchAsync(string reference, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Failing.Contains(reference) ? null : Materials.GetValueOrDefault(reference));
    }

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _provider;
    private readonly FakeFetcher _fetcher = new();

    public TlsSecretsHubTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
    }

    private TlsSecretsHub Hub(ISecretFetcher? fetcher) => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new TlsSecretsOptions()),
        new FakeTimeProvider(),
        NullLogger<TlsSecretsHub>.Instance,
        fetcher);

    private async Task AddHostnameAsync(string name, string secretReference)
    {
        await using var db = _database.CreateContext();
        db.Hostnames.Add(new HostnameEntity { Name = name, Certificate = new CertificateSource { Secret = secretReference } });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task WithoutAFetcher_RunOnceAsync_StaysEmpty()
    {
        await AddHostnameAsync("api.example.com", "ns/cert");
        var hub = Hub(fetcher: null);

        await hub.RunOnceAsync(CancellationToken.None);

        hub.GetCurrent().Secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeclaredHostnameWithSecretSource_IsFetchedAndMadeAvailable()
    {
        await AddHostnameAsync("api.example.com", "ns/cert");
        _fetcher.Materials["ns/cert"] = new CertificateMaterial("cert-pem", "key-pem");
        var hub = Hub(_fetcher);

        await hub.RunOnceAsync(CancellationToken.None);

        hub.GetCurrent().Secrets.ContainsKey("ns/cert").ShouldBeTrue();
        var material = hub.GetCurrent().Secrets["ns/cert"];
        material.CertificatePem.ShouldBe("cert-pem");
        material.KeyPem.ShouldBe("key-pem");
    }

    [Fact]
    public async Task Subscriber_IsNotifiedOnChange_ButNotWhenNothingChanged()
    {
        await AddHostnameAsync("api.example.com", "ns/cert");
        _fetcher.Materials["ns/cert"] = new CertificateMaterial("cert-pem", "key-pem");
        var hub = Hub(_fetcher);
        using var subscription = hub.Subscribe();

        await hub.RunOnceAsync(CancellationToken.None);
        subscription.Reader.TryRead(out var first).ShouldBeTrue();
        first!.Secrets.ContainsKey("ns/cert").ShouldBeTrue();

        await hub.RunOnceAsync(CancellationToken.None);
        subscription.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task FailedFetch_KeepsThePreviouslyLoadedMaterial()
    {
        await AddHostnameAsync("api.example.com", "ns/cert");
        _fetcher.Materials["ns/cert"] = new CertificateMaterial("cert-pem", "key-pem");
        var hub = Hub(_fetcher);
        await hub.RunOnceAsync(CancellationToken.None);

        _fetcher.Materials.Remove("ns/cert");
        _fetcher.Failing.Add("ns/cert");
        await hub.RunOnceAsync(CancellationToken.None);

        hub.GetCurrent().Secrets["ns/cert"].CertificatePem.ShouldBe("cert-pem");
    }

    [Fact]
    public async Task HostnameNoLongerDeclared_DropsItsSecret()
    {
        await AddHostnameAsync("api.example.com", "ns/cert");
        _fetcher.Materials["ns/cert"] = new CertificateMaterial("cert-pem", "key-pem");
        var hub = Hub(_fetcher);
        await hub.RunOnceAsync(CancellationToken.None);
        hub.GetCurrent().Secrets.ContainsKey("ns/cert").ShouldBeTrue();

        await using (var db = _database.CreateContext())
        {
            db.Hostnames.RemoveRange(db.Hostnames);
            await db.SaveChangesAsync();
        }

        await hub.RunOnceAsync(CancellationToken.None);

        hub.GetCurrent().Secrets.ShouldBeEmpty();
    }
}
