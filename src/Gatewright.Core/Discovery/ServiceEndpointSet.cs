// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace Gatewright.Core.Discovery;

/// <summary>The ready addresses behind each discovered service, keyed by service reference (<c>namespace/name:port</c>). Immutable.</summary>
public sealed class ServiceEndpointSet
{
    /// <summary>Gets an empty set.</summary>
    public static ServiceEndpointSet Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>Initializes a new instance of the <see cref="ServiceEndpointSet"/> class.</summary>
    /// <param name="addresses">The addresses (<c>http://10.0.0.5:8080</c>) per service reference.</param>
    public ServiceEndpointSet(IReadOnlyDictionary<string, IReadOnlyList<string>> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        Addresses = addresses.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Gets the addresses per service reference, sorted.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Addresses { get; }

    /// <summary>Gets the addresses of a service reference.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The addresses; empty when unknown.</returns>
    public IReadOnlyList<string> For(string reference) => Addresses.TryGetValue(reference, out var found) ? found : [];

    /// <summary>Computes a stable hash of the content, to detect changes cheaply.</summary>
    /// <returns>A lowercase hexadecimal SHA-256.</returns>
    public string Hash()
    {
        var builder = new StringBuilder();
        foreach (var (reference, addresses) in Addresses)
        {
            builder.Append(reference).Append('=').AppendJoin(',', addresses).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
