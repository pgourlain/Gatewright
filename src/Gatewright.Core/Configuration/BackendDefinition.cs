// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>Where requests of an API are forwarded.</summary>
public sealed record BackendDefinition
{
    /// <summary>Gets static destination base URLs.</summary>
    public IReadOnlyList<string> Destinations { get; init; } = [];

    /// <summary>Gets a reference to a discovered service (<c>namespace/name:port</c>), used instead of static destinations.</summary>
    public string? Service { get; init; }

    /// <summary>
    /// Gets the health checks. When not set, backends that reference a discovered service get passive checks so that a pod that
    /// fails is taken out of rotation; static destinations get none.
    /// </summary>
    public HealthCheckDefinition? HealthCheck { get; init; }

    /// <summary>Gets the backend request timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Gets the HTTP version used toward the backend (<c>1.1</c>, <c>2</c>), or <see langword="null"/> for the default.</summary>
    public string? HttpVersion { get; init; }
}
