// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Exposure;

/// <summary>Document-level gateway settings written to the <c>x-gateway</c> extension of the OpenAPI document.</summary>
public sealed class GatewayExposureOptions
{
    /// <summary>Gets or sets the API name used by the gateway. Defaults to a slug of the document title.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the base path under which the gateway exposes the API.</summary>
    public string? BasePath { get; set; }

    /// <summary>Gets or sets the products the API belongs to.</summary>
    public IList<string> Products { get; } = [];

    /// <summary>Gets or sets the <c>namespace/name:port</c> of the Kubernetes service that serves this API, when it should not be taken from the annotated Service.</summary>
    public string? Service { get; set; }
}
