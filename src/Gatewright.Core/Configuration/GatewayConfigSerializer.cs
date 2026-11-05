// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Gatewright.Core.Configuration;

/// <summary>Reads and writes <see cref="GatewayConfig"/> as JSON.</summary>
/// <remarks>
/// Reflection-based on purpose: the System.Text.Json source generator assigns <see langword="null"/> to init-only properties
/// missing from the document, which would defeat the empty-collection defaults of the model. Configuration is
/// (de)serialized when loading or publishing, never on the request path.
/// </remarks>
public static class GatewayConfigSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a configuration to indented JSON.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, Options);
    }

    /// <summary>Deserializes a configuration from JSON.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="JsonException">The JSON is invalid or does not match the model.</exception>
    public static GatewayConfig Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<GatewayConfig>(json, Options)
            ?? throw new JsonException("The configuration document is empty.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
