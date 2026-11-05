// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>A public hostname with its certificate source and HTTPS behavior.</summary>
public sealed class HostnameEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique hostname (or wildcard).</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the certificate source (file paths or secret reference, never key material).</summary>
    public CertificateSource? Certificate { get; set; }

    /// <summary>Gets or sets a value indicating whether HTTP is redirected to HTTPS.</summary>
    public bool RedirectHttpToHttps { get; set; }

    /// <summary>Gets or sets the HSTS settings.</summary>
    public HstsDefinition? Hsts { get; set; }
}
