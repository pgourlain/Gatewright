// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Tls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatewright.DataPlane.Tests.Tls;

public sealed class CertificateStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-certs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static HostnameDefinition Host(string name, (string CertPath, string KeyPath, string Thumbprint) cert) => new()
    {
        Name = name,
        Certificate = new CertificateSource { File = new CertificateFiles(cert.CertPath, cert.KeyPath) },
    };

    private static CertificateStore Store(params HostnameDefinition[] hostnames)
    {
        var store = new CertificateStore(NullLogger<CertificateStore>.Instance);
        store.Apply(new SecurityData { Hostnames = hostnames });
        return store;
    }

    [Fact]
    public void Select_ExactThenWildcardThenDefault()
    {
        var exact = TestCertificates.Write(_directory, "exact", "api.example.com");
        var wildcard = TestCertificates.Write(_directory, "wildcard", "*.example.com");
        var fallback = TestCertificates.Write(_directory, "fallback", "default.test");
        var store = Store(Host("api.example.com", exact), Host("*.example.com", wildcard), Host("*", fallback));

        store.Select("api.example.com")!.Thumbprint.ShouldBe(exact.Thumbprint);
        store.Select("API.Example.com")!.Thumbprint.ShouldBe(exact.Thumbprint);
        store.Select("shop.example.com")!.Thumbprint.ShouldBe(wildcard.Thumbprint);
        store.Select("deep.shop.example.com")!.Thumbprint.ShouldBe(fallback.Thumbprint);
        store.Select("other.org")!.Thumbprint.ShouldBe(fallback.Thumbprint);
        store.Select(null)!.Thumbprint.ShouldBe(fallback.Thumbprint);
    }

    [Fact]
    public void Select_NoDefault_UnknownHostHasNoCertificate()
    {
        var store = Store(Host("api.example.com", TestCertificates.Write(_directory, "a", "api.example.com")));

        store.Select("other.org").ShouldBeNull();
    }

    [Fact]
    public void ReloadChanged_NewFilesReplaceTheCertificate()
    {
        var first = TestCertificates.Write(_directory, "site", "api.example.com");
        var store = Store(Host("api.example.com", first));
        store.Select("api.example.com")!.Thumbprint.ShouldBe(first.Thumbprint);

        var second = TestCertificates.Write(_directory, "site", "api.example.com");
        store.ReloadChanged();

        second.Thumbprint.ShouldNotBe(first.Thumbprint);
        store.Select("api.example.com")!.Thumbprint.ShouldBe(second.Thumbprint);
    }

    [Fact]
    public void ReloadChanged_CorruptFiles_KeepTheWorkingCertificate()
    {
        var good = TestCertificates.Write(_directory, "site", "api.example.com");
        var store = Store(Host("api.example.com", good));

        File.WriteAllText(good.CertPath, "not a certificate");
        store.ReloadChanged();

        store.Select("api.example.com")!.Thumbprint.ShouldBe(good.Thumbprint);
    }

    [Fact]
    public void Apply_MissingFileAtStart_YieldsNoCertificateAndLaterAppears()
    {
        var path = Path.Combine(_directory, "late.crt");
        var keyPath = Path.Combine(_directory, "late.key");
        var store = Store(new HostnameDefinition { Name = "api.example.com", Certificate = new CertificateSource { File = new CertificateFiles(path, keyPath) } });
        store.Select("api.example.com").ShouldBeNull();

        var written = TestCertificates.Write(_directory, "late", "api.example.com");
        store.ReloadChanged();

        store.Select("api.example.com")!.Thumbprint.ShouldBe(written.Thumbprint);
    }

    [Fact]
    public void Apply_SameSourceTwice_KeepsTheLoadedCertificate()
    {
        var cert = TestCertificates.Write(_directory, "site", "api.example.com");
        var store = Store(Host("api.example.com", cert));
        var before = store.Select("api.example.com");

        store.Apply(new SecurityData { Hostnames = [Host("api.example.com", cert)] });

        store.Select("api.example.com").ShouldBeSameAs(before);
    }

    [Fact]
    public void SecretMaterial_PushedByTheControlPlane_IsUsed()
    {
        var cert = TestCertificates.Write(_directory, "secret", "api.example.com");
        var store = new CertificateStore(NullLogger<CertificateStore>.Instance);
        store.Apply(new SecurityData { Hostnames = [new HostnameDefinition { Name = "api.example.com", Certificate = new CertificateSource { Secret = "ns/tls" } }] });
        store.Select("api.example.com").ShouldBeNull();

        store.SetSecret("ns/tls", File.ReadAllText(cert.CertPath), File.ReadAllText(cert.KeyPath));

        store.Select("api.example.com")!.Thumbprint.ShouldBe(cert.Thumbprint);
    }

    [Fact]
    public void FindHostname_ExactWildcardAndDefault()
    {
        var cert = TestCertificates.Write(_directory, "any", "x.test");
        var store = Store(Host("api.example.com", cert), Host("*.example.com", cert), Host("*", cert));

        store.FindHostname("api.example.com")!.Name.ShouldBe("api.example.com");
        store.FindHostname("shop.example.com")!.Name.ShouldBe("*.example.com");
        store.FindHostname("elsewhere.org")!.Name.ShouldBe("*");
    }
}
