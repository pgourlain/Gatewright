// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Generates API keys from a cryptographically secure source.</summary>
public static class KeyGenerator
{
    /// <summary>Generates a key: <c>gw_</c> followed by 256 random bits in URL-safe Base64.</summary>
    /// <returns>The plaintext key. It must be shown to the user once and stored only as a hash.</returns>
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "gw_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
