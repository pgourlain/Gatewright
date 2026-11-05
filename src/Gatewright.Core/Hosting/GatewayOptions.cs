// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Hosting;

/// <summary>Top-level options, bound from the <c>Gateway</c> configuration section.</summary>
public sealed class GatewayOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Gateway";

    /// <summary>Gets or sets the process role.</summary>
    public GatewayMode Mode { get; set; } = GatewayMode.All;

    /// <summary>Gets or sets where the configuration is read from.</summary>
    public ConfigSourceKind ConfigSource { get; set; } = ConfigSourceKind.File;

    /// <summary>Gets or sets the configuration file path when <see cref="ConfigSource"/> is <see cref="ConfigSourceKind.File"/>.</summary>
    public string? ConfigFile { get; set; }

    /// <summary>Gets or sets the environment whose active publication the gateway serves when <see cref="ConfigSource"/> is <see cref="ConfigSourceKind.Database"/>.</summary>
    public string Environment { get; set; } = "dev";

    /// <summary>Gets or sets the bearer token protecting <c>/gw/config</c>. The endpoint is disabled when empty. Provide it through an environment variable or a secret, never a committed file.</summary>
    public string? DiagnosticsToken { get; set; }

    /// <summary>Gets or sets the secret pepper used to hash API keys. Provide it through a secret store or environment variable; it must match the pepper used to produce the stored key hashes.</summary>
    public string? ApiKeyPepper { get; set; }

    /// <summary>Gets or sets how often the configuration file is checked for changes.</summary>
    public TimeSpan ConfigPollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
