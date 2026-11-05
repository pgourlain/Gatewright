// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>A consumer's subscription to a product (and optionally a plan), identified by up to two active keys.</summary>
public sealed class SubscriptionEntity
{
    /// <summary>The maximum number of simultaneously valid keys (primary and secondary).</summary>
    public const int MaxActiveKeys = 2;

    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the unique name used as subscription id in the gateway configuration.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the consumer identifier.</summary>
    public Guid ConsumerId { get; set; }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the plan identifier.</summary>
    public Guid? PlanId { get; set; }

    /// <summary>Gets or sets a value indicating whether the subscription is active.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Gets the keys, valid or revoked.</summary>
    public List<SubscriptionKey> Keys { get; } = [];

    /// <summary>Gets the keys that are not revoked.</summary>
    public IEnumerable<SubscriptionKey> ActiveKeys => Keys.Where(k => k.RevokedAt is null);

    /// <summary>Adds a key (stored as hash only).</summary>
    /// <param name="hash">The key hash.</param>
    /// <param name="label">The label (<c>primary</c>, <c>secondary</c>…).</param>
    /// <param name="now">The creation time.</param>
    /// <returns>The new key.</returns>
    /// <exception cref="InvalidOperationException">The subscription already has the maximum number of active keys.</exception>
    public SubscriptionKey AddKey(string hash, string label, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        if (ActiveKeys.Count() >= MaxActiveKeys)
        {
            throw new InvalidOperationException($"A subscription can have at most {MaxActiveKeys} active keys; revoke one first.");
        }

        if (ActiveKeys.Any(k => k.Label == label))
        {
            throw new InvalidOperationException($"The subscription already has an active '{label}' key; revoke or rotate it first.");
        }

        var key = new SubscriptionKey { SubscriptionId = Id, Hash = hash, Label = label, CreatedAt = now };
        Keys.Add(key);
        return key;
    }

    /// <summary>Revokes the active key with the given label.</summary>
    /// <param name="label">The key label.</param>
    /// <param name="now">The revocation time.</param>
    /// <returns><see langword="true"/> when a key was revoked.</returns>
    public bool RevokeKey(string label, DateTimeOffset now)
    {
        var key = ActiveKeys.FirstOrDefault(k => k.Label == label);
        if (key is null)
        {
            return false;
        }

        key.RevokedAt = now;
        return true;
    }
}
