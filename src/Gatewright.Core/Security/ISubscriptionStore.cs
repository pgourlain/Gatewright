// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Core.Security;

/// <summary>Resolves a subscription from the hash of an API key. Implementations must answer from local memory on the request path.</summary>
public interface ISubscriptionStore
{
    /// <summary>Finds the subscription owning a key hash.</summary>
    /// <param name="keyHash">The lowercase hexadecimal hash produced by <see cref="ApiKeyHasher"/>.</param>
    /// <returns>The subscription, or <see langword="null"/> when unknown.</returns>
    ValueTask<SubscriptionInfo?> FindByKeyHashAsync(string keyHash);
}
