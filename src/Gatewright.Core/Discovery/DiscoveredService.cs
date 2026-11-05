// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>A service found by discovery (for example a Kubernetes Service annotated <c>gatewright.io/expose</c>).</summary>
/// <param name="Namespace">The namespace.</param>
/// <param name="Name">The service name.</param>
/// <param name="Ports">The service ports that can be referenced as <c>namespace/name:port</c>.</param>
/// <param name="OpenApiPath">The path of its OpenAPI document (<c>gatewright.io/openapi-path</c>), if any.</param>
/// <param name="BasePath">The base path requested for its API (<c>gatewright.io/base-path</c>), if any.</param>
/// <param name="Product">The product requested for its API (<c>gatewright.io/product</c>), if any.</param>
public sealed record DiscoveredService(
    string Namespace,
    string Name,
    IReadOnlyList<int> Ports,
    string? OpenApiPath = null,
    string? BasePath = null,
    string? Product = null)
{
    /// <summary>Builds the reference of one of its ports, as used in <c>BackendDefinition.Service</c>.</summary>
    /// <param name="port">The service port.</param>
    /// <returns>A reference such as <c>shop/orders:8080</c>.</returns>
    public string Reference(int port) => $"{Namespace}/{Name}:{port}";
}
