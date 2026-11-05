// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>A public hostname served by the gateway: its certificate and its HTTPS behavior.</summary>
public sealed record HostnameDefinition
{
    /// <summary>Gets the hostname: <c>api.example.com</c>, a wildcard such as <c>*.example.com</c> (one label), or <c>*</c> for the default certificate.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the certificate source. Required when the gateway terminates TLS for this hostname.</summary>
    public CertificateSource? Certificate { get; init; }

    /// <summary>Gets a value indicating whether plain HTTP requests for this hostname are redirected to HTTPS.</summary>
    public bool RedirectHttpToHttps { get; init; }

    /// <summary>Gets the HSTS header to send on HTTPS responses, or <see langword="null"/> for none.</summary>
    public HstsDefinition? Hsts { get; init; }
}

/// <summary>Where a certificate comes from. Exactly one member is set. Private keys never appear in the configuration.</summary>
public sealed record CertificateSource
{
    /// <summary>Gets PEM files readable by the gateway, typically a mounted Kubernetes TLS secret. They are reloaded when they change.</summary>
    public CertificateFiles? File { get; init; }

    /// <summary>Gets the reference <c>namespace/name</c> of a Kubernetes TLS secret read by the control plane and pushed to the data planes.</summary>
    public string? Secret { get; init; }
}

/// <summary>A PEM certificate chain and its private key on disk.</summary>
/// <param name="CertPath">The certificate (and chain) file, for example <c>tls.crt</c>.</param>
/// <param name="KeyPath">The private key file, for example <c>tls.key</c>.</param>
public sealed record CertificateFiles(string CertPath, string KeyPath);

/// <summary>HTTP Strict Transport Security settings.</summary>
public sealed record HstsDefinition
{
    /// <summary>Gets the <c>max-age</c> in seconds.</summary>
    public int MaxAgeSeconds { get; init; } = 31_536_000;

    /// <summary>Gets a value indicating whether <c>includeSubDomains</c> is sent.</summary>
    public bool IncludeSubDomains { get; init; }

    /// <summary>Gets a value indicating whether <c>preload</c> is sent.</summary>
    public bool Preload { get; init; }
}
