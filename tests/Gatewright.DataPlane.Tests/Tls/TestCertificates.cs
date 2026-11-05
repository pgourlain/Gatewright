// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Gatewright.DataPlane.Tests.Tls;

/// <summary>Creates self-signed certificates and writes them as PEM files, like a mounted Kubernetes TLS secret.</summary>
internal static class TestCertificates
{
    public static (string CertPath, string KeyPath, string Thumbprint) Write(string directory, string name, params string[] dnsNames)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsNames[0]}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var dns in dnsNames)
        {
            san.AddDnsName(dns);
        }

        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        var certPath = Path.Combine(directory, name + ".crt");
        var keyPath = Path.Combine(directory, name + ".key");
        File.WriteAllText(certPath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        return (certPath, keyPath, certificate.Thumbprint);
    }
}
