// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Settings for fetching the Kubernetes secrets referenced by declared hostnames, bound from <c>Kubernetes:Secrets</c>.</summary>
public sealed class TlsSecretsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Kubernetes:Secrets";

    /// <summary>Gets or sets how often the declared hostnames are checked for secret references and re-fetched, in seconds.</summary>
    public int PollSeconds { get; set; } = 10;
}
