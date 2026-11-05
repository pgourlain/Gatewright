// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.DataPlane.Tls;

/// <summary>TLS termination settings, bound from <c>Gateway:Tls</c>. Public TLS may also be terminated upstream (Front Door, Application Gateway): leave <see cref="HttpsPort"/> at 0 then.</summary>
public sealed class TlsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Gateway:Tls";

    /// <summary>Gets or sets the HTTPS port. 0 disables TLS termination.</summary>
    public int HttpsPort { get; set; }

    /// <summary>Gets or sets the plain HTTP port used when <see cref="HttpsPort"/> is set (Kestrel needs both declared explicitly).</summary>
    public int HttpPort { get; set; } = 8080;

    /// <summary>Gets or sets how often certificate files are checked for changes, in seconds.</summary>
    public int ReloadSeconds { get; set; } = 30;

    /// <summary>Gets or sets the public HTTPS port used in redirects when it differs from 443 (for example behind a NodePort).</summary>
    public int? RedirectPort { get; set; }
}
