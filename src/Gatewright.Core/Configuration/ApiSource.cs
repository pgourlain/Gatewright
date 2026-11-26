// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>Where an API comes from when it is not declared by hand (ADR 0017).</summary>
public sealed record ApiSource
{
    /// <summary>The kind of an API synchronized from a Kubernetes Gateway API <c>HTTPRoute</c>.</summary>
    public const string GatewayApi = "gatewayapi";

    /// <summary>Gets the kind of source.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the reference of the source object, such as <c>namespace/name</c> of the route.</summary>
    public required string Ref { get; init; }

    /// <summary>Gets the product requested by the source object (the <c>gatewright.io/product</c> annotation), or <see langword="null"/>.</summary>
    public string? Product { get; init; }
}
