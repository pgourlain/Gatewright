// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace Gatewright.Core.Security;

/// <summary>Hashes API keys with HMAC-SHA256 keyed by a secret pepper, so a leaked store of hashes cannot be brute-forced offline without the pepper.</summary>
public sealed class ApiKeyHasher
{
    private readonly byte[] _pepper;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyHasher"/> class.</summary>
    /// <param name="pepper">The secret pepper; resolve it from a secret store, never commit it. Empty is accepted for local development only.</param>
    public ApiKeyHasher(string? pepper) => _pepper = Encoding.UTF8.GetBytes(pepper ?? "");

    /// <summary>Hashes a key.</summary>
    /// <param name="key">The plaintext key.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public string Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(key)));
    }
}
