// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Snapshots;

namespace Gatewright.Core.Security;

/// <summary>In-memory subscription store fed by snapshots (dev mode and local cache). The index is swapped atomically.</summary>
public sealed class InMemorySubscriptionStore : ISubscriptionStore, ISecurityDataSink, IRevocationSink
{
    private Dictionary<string, SubscriptionInfo> _byHash = [];
    private HashSet<string> _revoked = [];

    /// <inheritdoc/>
    public ValueTask<SubscriptionInfo?> FindByKeyHashAsync(string keyHash)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        // Lookup is by keyed hash (HMAC with a secret pepper): an attacker cannot choose or predict the hash being compared,
        // so the dictionary lookup leaks nothing exploitable about stored keys. The plaintext key is never compared.
        var index = Volatile.Read(ref _byHash);
        if (!index.TryGetValue(keyHash, out var candidate) || Volatile.Read(ref _revoked).Contains(keyHash))
        {
            return new ValueTask<SubscriptionInfo?>((SubscriptionInfo?)null);
        }

        return new ValueTask<SubscriptionInfo?>(candidate);
    }

    /// <inheritdoc/>
    public void Apply(SecurityData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var index = new Dictionary<string, SubscriptionInfo>(StringComparer.Ordinal);
        foreach (var subscription in data.Subscriptions)
        {
            var info = new SubscriptionInfo(subscription.Id, subscription.Product, subscription.Plan, subscription.Active);
            foreach (var hash in subscription.KeyHashes)
            {
                index[hash.ToLowerInvariant()] = info;
            }
        }

        Volatile.Write(ref _byHash, index);
        Volatile.Write(ref _revoked, []);
    }

    /// <inheritdoc/>
    public void Revoke(string keyHash)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        var normalized = keyHash.ToLowerInvariant();
        lock (this)
        {
            var next = new HashSet<string>(Volatile.Read(ref _revoked), StringComparer.Ordinal) { normalized };
            Volatile.Write(ref _revoked, next);
        }
    }
}
