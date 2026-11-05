// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Gatewright.Core.Configuration;

/// <summary>JSON helpers using the configuration conventions (camelCase, string enums, nulls omitted) for any configuration type.</summary>
public static class GatewayJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Gets the shared serializer options (read-only).</summary>
    public static JsonSerializerOptions SerializerOptions => Options;

    /// <summary>Serializes a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>Compact JSON.</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserializes a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="JsonException">The JSON is invalid or empty.</exception>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("The document is empty.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
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
