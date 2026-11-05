// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Distribution;

/// <summary>Computes and applies <see cref="ConfigDelta"/>s. <c>Apply(old, Compute(old, new))</c> yields a configuration equal to <c>new</c> (same content hash).</summary>
public static class ConfigDeltaCalculator
{
    /// <summary>Computes what changed between two configurations.</summary>
    /// <param name="before">The older configuration.</param>
    /// <param name="after">The newer configuration.</param>
    /// <returns>The delta.</returns>
    public static ConfigDelta Compute(GatewayConfig before, GatewayConfig after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var (apisUp, apisDown) = Diff(before.Apis, after.Apis, a => a.Name);
        var (productsUp, productsDown) = Diff(before.Products, after.Products, p => p.Name);
        var (subscriptionsUp, subscriptionsDown) = Diff(before.Subscriptions, after.Subscriptions, s => s.Id);

        var policiesUp = after.Policies
            .Where(p => !before.Policies.TryGetValue(p.Key, out var old) || Json(old) != Json(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);
        var policiesDown = before.Policies.Keys.Where(k => !after.Policies.ContainsKey(k)).ToList();

        return new ConfigDelta
        {
            ApisUpserted = apisUp,
            ApisRemoved = apisDown,
            ProductsUpserted = productsUp,
            ProductsRemoved = productsDown,
            SubscriptionsUpserted = subscriptionsUp,
            SubscriptionsRemoved = subscriptionsDown,
            PoliciesUpserted = policiesUp,
            PoliciesRemoved = policiesDown,
            GlobalPolicies = Json(before.GlobalPolicies) == Json(after.GlobalPolicies) ? null : after.GlobalPolicies,
            GatewayPolicies = Json(before.GatewayPolicies) == Json(after.GatewayPolicies) ? null : after.GatewayPolicies,
        };
    }

    /// <summary>Applies a delta to a configuration.</summary>
    /// <param name="config">The configuration the delta was computed from.</param>
    /// <param name="delta">The delta.</param>
    /// <param name="version">The version of the resulting configuration.</param>
    /// <returns>The resulting configuration.</returns>
    public static GatewayConfig Apply(GatewayConfig config, ConfigDelta delta, long version)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(delta);

        var policies = config.Policies.Where(p => !delta.PoliciesRemoved.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        foreach (var (name, policy) in delta.PoliciesUpserted)
        {
            policies[name] = policy;
        }

        return config with
        {
            Version = version,
            GlobalPolicies = delta.GlobalPolicies ?? config.GlobalPolicies,
            GatewayPolicies = delta.GatewayPolicies ?? config.GatewayPolicies,
            Policies = policies.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            Apis = Merge(config.Apis, delta.ApisUpserted, delta.ApisRemoved, a => a.Name),
            Products = Merge(config.Products, delta.ProductsUpserted, delta.ProductsRemoved, p => p.Name),
            Subscriptions = Merge(config.Subscriptions, delta.SubscriptionsUpserted, delta.SubscriptionsRemoved, s => s.Id),
        };
    }

    private static (List<T> Upserted, List<string> Removed) Diff<T>(IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, string> key)
    {
        var old = before.ToDictionary(key, StringComparer.Ordinal);
        var upserted = after.Where(item => !old.TryGetValue(key(item), out var previous) || Json(previous) != Json(item)).ToList();
        var names = after.Select(key).ToHashSet(StringComparer.Ordinal);
        return (upserted, [.. old.Keys.Where(k => !names.Contains(k))]);
    }

    private static List<T> Merge<T>(IReadOnlyList<T> current, IReadOnlyList<T> upserted, IReadOnlyList<string> removed, Func<T, string> key)
    {
        var map = current.Where(item => !removed.Contains(key(item))).ToDictionary(key, StringComparer.Ordinal);
        foreach (var item in upserted)
        {
            map[key(item)] = item;
        }

        // Same deterministic order as the exporter: by name.
        return [.. map.Values.OrderBy(key, StringComparer.Ordinal)];
    }

    private static string Json<T>(T value) => GatewayJson.Serialize(value);
}
