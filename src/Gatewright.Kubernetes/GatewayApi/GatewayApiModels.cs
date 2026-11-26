// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>Names of the Gateway API resources read by Gatewright.</summary>
public static class GatewayApiResources
{
    /// <summary>The API group.</summary>
    public const string Group = "gateway.networking.k8s.io";

    /// <summary>The API version.</summary>
    public const string Version = "v1";

    /// <summary>The plural name of <c>HTTPRoute</c>.</summary>
    public const string HttpRoutes = "httproutes";

    /// <summary>The plural name of <c>Gateway</c>.</summary>
    public const string Gateways = "gateways";
}

/// <summary>The part of a Gateway API <c>HTTPRoute</c> that Gatewright reads.</summary>
public sealed class HttpRoute : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc/>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = GatewayApiResources.Group + "/" + GatewayApiResources.Version;

    /// <inheritdoc/>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "HTTPRoute";

    /// <inheritdoc/>
    [JsonPropertyName("metadata")]
    public V1ObjectMeta Metadata { get; set; } = new();

    /// <summary>Gets or sets the specification.</summary>
    [JsonPropertyName("spec")]
    public HttpRouteSpec? Spec { get; set; }
}

/// <summary>The list of <see cref="HttpRoute"/> returned by the API server.</summary>
public sealed class HttpRouteList : IKubernetesObject<V1ListMeta>, IItems<HttpRoute>
{
    /// <inheritdoc/>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = GatewayApiResources.Group + "/" + GatewayApiResources.Version;

    /// <inheritdoc/>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "HTTPRouteList";

    /// <inheritdoc/>
    [JsonPropertyName("metadata")]
    public V1ListMeta Metadata { get; set; } = new();

    /// <inheritdoc/>
    [JsonPropertyName("items")]
    public IList<HttpRoute> Items { get; set; } = [];
}

/// <summary>The specification of an <see cref="HttpRoute"/>.</summary>
public sealed class HttpRouteSpec
{
    /// <summary>Gets or sets the Gateways the route attaches to.</summary>
    [JsonPropertyName("parentRefs")]
    public List<ParentRef>? ParentRefs { get; set; }

    /// <summary>Gets or sets the host names.</summary>
    [JsonPropertyName("hostnames")]
    public List<string>? Hostnames { get; set; }

    /// <summary>Gets or sets the rules.</summary>
    [JsonPropertyName("rules")]
    public List<HttpRouteRule>? Rules { get; set; }
}

/// <summary>A reference to the parent of a route.</summary>
public sealed class ParentRef
{
    /// <summary>Gets or sets the group (default <c>gateway.networking.k8s.io</c>).</summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    /// <summary>Gets or sets the kind (default <c>Gateway</c>).</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Gets or sets the namespace (default: the namespace of the route).</summary>
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>A rule of a route.</summary>
public sealed class HttpRouteRule
{
    /// <summary>Gets or sets the matches (an empty list means every path).</summary>
    [JsonPropertyName("matches")]
    public List<HttpRouteMatch>? Matches { get; set; }

    /// <summary>Gets or sets the filters. None is supported.</summary>
    [JsonPropertyName("filters")]
    public List<JsonElement>? Filters { get; set; }

    /// <summary>Gets or sets the backends.</summary>
    [JsonPropertyName("backendRefs")]
    public List<BackendRef>? BackendRefs { get; set; }

    /// <summary>Gets or sets the timeouts. None is supported.</summary>
    [JsonPropertyName("timeouts")]
    public JsonElement? Timeouts { get; set; }
}

/// <summary>A match of a rule.</summary>
public sealed class HttpRouteMatch
{
    /// <summary>Gets or sets the path match.</summary>
    [JsonPropertyName("path")]
    public PathMatch? Path { get; set; }

    /// <summary>Gets or sets the header matches. None is supported.</summary>
    [JsonPropertyName("headers")]
    public List<JsonElement>? Headers { get; set; }

    /// <summary>Gets or sets the query parameter matches. None is supported.</summary>
    [JsonPropertyName("queryParams")]
    public List<JsonElement>? QueryParams { get; set; }

    /// <summary>Gets or sets the HTTP method.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

/// <summary>A path match.</summary>
public sealed class PathMatch
{
    /// <summary>Gets or sets the type: <c>Exact</c>, <c>PathPrefix</c> (default) or <c>RegularExpression</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the value (default <c>/</c>).</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>A backend reference.</summary>
public sealed class BackendRef
{
    /// <summary>Gets or sets the group (default: the core group).</summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    /// <summary>Gets or sets the kind (default <c>Service</c>).</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the namespace (default: the namespace of the route).</summary>
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    /// <summary>Gets or sets the port.</summary>
    [JsonPropertyName("port")]
    public int? Port { get; set; }

    /// <summary>Gets or sets the weight.</summary>
    [JsonPropertyName("weight")]
    public int? Weight { get; set; }

    /// <summary>Gets or sets the filters. None is supported.</summary>
    [JsonPropertyName("filters")]
    public List<JsonElement>? Filters { get; set; }
}

/// <summary>The part of a Gateway API <c>Gateway</c> that Gatewright reads.</summary>
public sealed class Gateway : IKubernetesObject<V1ObjectMeta>
{
    /// <inheritdoc/>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = GatewayApiResources.Group + "/" + GatewayApiResources.Version;

    /// <inheritdoc/>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Gateway";

    /// <inheritdoc/>
    [JsonPropertyName("metadata")]
    public V1ObjectMeta Metadata { get; set; } = new();

    /// <summary>Gets or sets the specification.</summary>
    [JsonPropertyName("spec")]
    public GatewaySpec? Spec { get; set; }
}

/// <summary>The specification of a <see cref="Gateway"/>.</summary>
public sealed class GatewaySpec
{
    /// <summary>Gets or sets the class of the Gateway.</summary>
    [JsonPropertyName("gatewayClassName")]
    public string? GatewayClassName { get; set; }
}

/// <summary>The list of <see cref="Gateway"/> returned by the API server.</summary>
public sealed class GatewayList : IKubernetesObject<V1ListMeta>, IItems<Gateway>
{
    /// <inheritdoc/>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = GatewayApiResources.Group + "/" + GatewayApiResources.Version;

    /// <inheritdoc/>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "GatewayList";

    /// <inheritdoc/>
    [JsonPropertyName("metadata")]
    public V1ListMeta Metadata { get; set; } = new();

    /// <inheritdoc/>
    [JsonPropertyName("items")]
    public IList<Gateway> Items { get; set; } = [];
}
