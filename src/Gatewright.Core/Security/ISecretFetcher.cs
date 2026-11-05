// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>Fetches the certificate material of a Kubernetes TLS secret referenced by a hostname. Implemented by the Kubernetes integration.</summary>
public interface ISecretFetcher
{
    /// <summary>Fetches the certificate chain and private key of a secret.</summary>
    /// <param name="reference">The secret reference, <c>namespace/name</c>.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The material, or <see langword="null"/> when the secret does not exist or could not be read.</returns>
    ValueTask<CertificateMaterial?> FetchAsync(string reference, CancellationToken cancellationToken);
}

/// <summary>A certificate chain and private key in PEM. Held in memory only, never persisted.</summary>
/// <param name="CertificatePem">The certificate chain, PEM encoded.</param>
/// <param name="KeyPem">The private key, PEM encoded.</param>
public sealed record CertificateMaterial(string CertificatePem, string KeyPem);
