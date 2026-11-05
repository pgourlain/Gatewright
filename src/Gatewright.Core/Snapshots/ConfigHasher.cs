// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gatewright.Core.Configuration;

namespace Gatewright.Core.Snapshots;

/// <summary>Computes a stable content hash of a configuration.</summary>
public static class ConfigHasher
{
    /// <summary>Hashes a configuration. Object property order and the <see cref="GatewayConfig.Version"/> do not influence the result.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>A lowercase hexadecimal SHA-256.</returns>
    public static string Hash(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var node = JsonNode.Parse(GatewayConfigSerializer.Serialize(config))!.AsObject();
        node.Remove("version");
        var canonical = Canonicalize(node);
        var bytes = Encoding.UTF8.GetBytes(canonical!.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var key in obj.Select(p => p.Key).Order(StringComparer.Ordinal).ToList())
                {
                    sorted[key] = Canonicalize(obj[key]);
                }

                return sorted;
            case JsonArray array:
                var copy = new JsonArray();
                foreach (var item in array)
                {
                    copy.Add(Canonicalize(item));
                }

                return copy;
            case null:
                return null;
            default:
                return node.DeepClone();
        }
    }
}
