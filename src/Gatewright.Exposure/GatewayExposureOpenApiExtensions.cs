// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Gatewright.Exposure;

/// <summary>Writes the gateway policies declared in code into the <c>x-gateway</c> extensions of the native ASP.NET Core OpenAPI document.</summary>
public static class GatewayExposureOpenApiExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Adds the <c>x-gateway</c> transformers to the OpenAPI document generator:
    /// <c>services.AddOpenApi(o =&gt; o.AddGatewayExposure(g =&gt; g.BasePath = "/orders"))</c>.
    /// </summary>
    /// <param name="options">The OpenAPI options.</param>
    /// <param name="configure">Document-level settings.</param>
    /// <returns>The options.</returns>
    public static OpenApiOptions AddGatewayExposure(this OpenApiOptions options, Action<GatewayExposureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var exposure = new GatewayExposureOptions();
        configure?.Invoke(exposure);

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            document.Extensions["x-gateway-version"] = new JsonNodeExtension(JsonValue.Create(1));
            var node = new JsonObject();
            if (exposure.Name is not null)
            {
                node["name"] = exposure.Name;
            }

            if (exposure.BasePath is not null)
            {
                node["basePath"] = exposure.BasePath;
            }

            if (exposure.Products.Count > 0)
            {
                node["products"] = new JsonArray([.. exposure.Products.Select(p => (JsonNode?)JsonValue.Create(p))]);
            }

            if (exposure.Service is not null)
            {
                node["backend"] = new JsonObject { ["service"] = exposure.Service };
            }

            document.Extensions["x-gateway"] = new JsonNodeExtension(node);
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var policies = context.Description.ActionDescriptor.EndpointMetadata
                .SelectMany(m => m switch
                {
                    GatewayPolicyMetadata metadata => [metadata],
                    IGatewayPolicyMetadataSource source => [source.ToMetadata()],
                    _ => Array.Empty<GatewayPolicyMetadata>(),
                })
                .ToList();
            if (policies.Count == 0)
            {
                return Task.CompletedTask;
            }

            var extension = new JsonObject();
            var generic = new JsonObject();
            foreach (var policy in policies)
            {
                var configuration = policy.Configuration is null ? new JsonObject() : JsonSerializer.SerializeToNode(policy.Configuration, Json);
                if (BuiltInShortcuts.Contains(policy.Type) && policy.Phase == "inbound" || policy.Type == "cors")
                {
                    extension[policy.Type] = configuration;
                    continue;
                }

                var phase = generic[policy.Phase] as JsonArray ?? new JsonArray();
                phase.Add(new JsonObject { ["type"] = policy.Type, ["config"] = configuration });
                generic[policy.Phase] = phase;
            }

            if (generic.Count > 0)
            {
                extension["policies"] = generic;
            }

            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions["x-gateway"] = new JsonNodeExtension(extension);
            return Task.CompletedTask;
        });
        return options;
    }

    private static readonly HashSet<string> BuiltInShortcuts =
    [
        "jwt", "apiKey", "ipFilter", "rateLimit", "quota", "cache", "rewrite", "mockResponse", "timeout", "retry", "circuitBreaker",
    ];
}
