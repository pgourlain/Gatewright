// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace Gatewright.Core.Security;

/// <summary>The certificate material of every hostname whose certificate source is a Kubernetes secret, keyed by secret reference (<c>namespace/name</c>). Immutable, never persisted.</summary>
public sealed class CertificateMaterialSet
{
    /// <summary>Gets an empty set.</summary>
    public static CertificateMaterialSet Empty { get; } = new(new Dictionary<string, CertificateMaterial>());

    /// <summary>Initializes a new instance of the <see cref="CertificateMaterialSet"/> class.</summary>
    /// <param name="secrets">The certificate material per secret reference.</param>
    public CertificateMaterialSet(IReadOnlyDictionary<string, CertificateMaterial> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        Secrets = secrets.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>Gets the certificate material per secret reference, sorted.</summary>
    public IReadOnlyDictionary<string, CertificateMaterial> Secrets { get; }

    /// <summary>Computes a stable hash of the content, to detect changes cheaply without logging the material itself.</summary>
    /// <returns>A lowercase hexadecimal SHA-256.</returns>
    public string Hash()
    {
        var builder = new StringBuilder();
        foreach (var (reference, material) in Secrets)
        {
            builder.Append(reference).Append('=').Append(material.CertificatePem).Append('|').Append(material.KeyPem).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
