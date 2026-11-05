// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Identifies the API a request was routed to.</summary>
public sealed class ApiInfo
{
    /// <summary>Initializes a new instance of the <see cref="ApiInfo"/> class.</summary>
    /// <param name="name">The unique API name.</param>
    /// <param name="basePath">The base path under which the API is exposed.</param>
    /// <param name="version">The API version, or <see langword="null"/>.</param>
    public ApiInfo(string name, string basePath, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(basePath);
        Name = name;
        BasePath = basePath;
        Version = version;
    }

    /// <summary>Gets the unique API name.</summary>
    public string Name { get; }

    /// <summary>Gets the base path under which the API is exposed.</summary>
    public string BasePath { get; }

    /// <summary>Gets the API version.</summary>
    public string? Version { get; }
}
