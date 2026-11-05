// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>The kind of a difference between two configurations.</summary>
public enum DiffKind
{
    /// <summary>The item exists only in the newer configuration.</summary>
    Added,

    /// <summary>The item exists only in the older configuration.</summary>
    Removed,

    /// <summary>The item exists in both with different values.</summary>
    Changed,
}

/// <summary>One difference between two configurations.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Path">Where, for example <c>apis.orders.backend.timeout</c>.</param>
/// <param name="Before">The older value as JSON, or <see langword="null"/> when added.</param>
/// <param name="After">The newer value as JSON, or <see langword="null"/> when removed.</param>
public sealed record DiffEntry(DiffKind Kind, string Path, string? Before, string? After);

/// <summary>Computes the structural difference between two configurations. Lists of named items (APIs, operations, products, subscriptions) are matched by name.</summary>
public static class ConfigDiff
{
    /// <summary>Compares two configurations. The <c>version</c> is ignored.</summary>
    /// <param name="before">The older configuration.</param>
    /// <param name="after">The newer configuration.</param>
    /// <returns>The differences, ordered by path.</returns>
    public static IReadOnlyList<DiffEntry> Compare(GatewayConfig before, GatewayConfig after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var entries = new List<DiffEntry>();
        Walk(Normalize(before), Normalize(after), "", entries);
        return [.. entries.OrderBy(e => e.Path, StringComparer.Ordinal)];
    }

    private static JsonObject Normalize(GatewayConfig config)
    {
        var root = JsonNode.Parse(GatewayConfigSerializer.Serialize(config))!.AsObject();
        root.Remove("version");
        KeyByName(root, "products", "name");
        KeyByName(root, "subscriptions", "id");
        KeyByName(root, "apis", "name");
        if (root["apis"] is JsonObject apis)
        {
            foreach (var (_, api) in apis)
            {
                if (api is JsonObject apiObject)
                {
                    KeyByName(apiObject, "operations", "name");
                }
            }
        }

        return root;
    }

    private static void KeyByName(JsonObject parent, string property, string key)
    {
        if (parent[property] is not JsonArray array)
        {
            return;
        }

        var keyed = new JsonObject();
        foreach (var item in array)
        {
            if (item is JsonObject obj && obj[key]?.GetValue<string>() is { } name)
            {
                keyed[name] = obj.DeepClone();
            }
        }

        parent[property] = keyed;
    }

    private static void Walk(JsonNode? before, JsonNode? after, string path, List<DiffEntry> entries)
    {
        if (before is JsonObject b && after is JsonObject a)
        {
            foreach (var key in b.Select(p => p.Key).Union(a.Select(p => p.Key)).Distinct())
            {
                var child = path.Length == 0 ? key : path + "." + key;
                Walk(b[key], a[key], child, entries);
            }

            return;
        }

        if (before is JsonArray ba && after is JsonArray aa)
        {
            for (var i = 0; i < Math.Max(ba.Count, aa.Count); i++)
            {
                Walk(i < ba.Count ? ba[i] : null, i < aa.Count ? aa[i] : null, $"{path}[{i}]", entries);
            }

            return;
        }

        if (before is null && after is null)
        {
            return;
        }

        if (before is null)
        {
            entries.Add(new DiffEntry(DiffKind.Added, path, null, after!.ToJsonString()));
        }
        else if (after is null)
        {
            entries.Add(new DiffEntry(DiffKind.Removed, path, before.ToJsonString(), null));
        }
        else if (!JsonNode.DeepEquals(before, after))
        {
            entries.Add(new DiffEntry(DiffKind.Changed, path, before.ToJsonString(), after.ToJsonString()));
        }
    }
}
