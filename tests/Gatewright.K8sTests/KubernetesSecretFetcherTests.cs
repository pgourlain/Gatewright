// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatewright.Kubernetes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatewright.K8sTests;

[Collection(KindTestGroup.Name)]
public sealed class KubernetesSecretFetcherTests(KindFixture kind) : IAsyncLifetime
{
    private readonly string _namespace = "gw-" + Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") != "1")
        {
            return;
        }

        await KindFixture.KubectlAsync(null, "create", "namespace", _namespace);
    }

    public async Task DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") == "1")
        {
            await KindFixture.KubectlAsync(null, "delete", "namespace", _namespace, "--wait=false");
        }
    }

    private static (string CertPem, string KeyPem) SelfSignedCertificate(string dnsName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        return (certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }

    [K8sFact]
    public async Task FetchAsync_ReadsATlsSecret_AndReturnsNullForAMissingOne()
    {
        var (certPem, keyPem) = SelfSignedCertificate("api.example.com");
        var directory = Directory.CreateTempSubdirectory("gatewright-secret-");
        try
        {
            var certPath = Path.Combine(directory.FullName, "tls.crt");
            var keyPath = Path.Combine(directory.FullName, "tls.key");
            await File.WriteAllTextAsync(certPath, certPem);
            await File.WriteAllTextAsync(keyPath, keyPem);
            await KindFixture.KubectlAsync(null, "-n", _namespace, "create", "secret", "tls", "site-tls", $"--cert={certPath}", $"--key={keyPath}");

            var fetcher = new KubernetesSecretFetcher(kind.Client, NullLogger<KubernetesSecretFetcher>.Instance);

            var material = await fetcher.FetchAsync($"{_namespace}/site-tls", CancellationToken.None);

            material.ShouldNotBeNull();
            material.CertificatePem.Trim().ShouldBe(certPem.Trim());
            material.KeyPem.Trim().ShouldBe(keyPem.Trim());

            (await fetcher.FetchAsync($"{_namespace}/missing", CancellationToken.None)).ShouldBeNull();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
