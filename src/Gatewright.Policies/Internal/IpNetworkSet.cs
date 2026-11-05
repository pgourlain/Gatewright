// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;

namespace Gatewright.Policies.Internal;

/// <summary>A set of IP networks parsed once; entries without a prefix match a single address.</summary>
internal sealed class IpNetworkSet
{
    private readonly IPNetwork[] _networks;

    public IpNetworkSet(IEnumerable<string> entries)
    {
        _networks = [.. entries.Select(Parse)];
    }

    public bool IsEmpty => _networks.Length == 0;

    public bool Contains(IPAddress address)
    {
        address = Normalize(address);
        foreach (var network in _networks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IPNetwork Parse(string entry)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            var network = IPNetwork.Parse(entry);
            return new IPNetwork(Normalize(network.BaseAddress), network.PrefixLength);
        }

        var address = Normalize(IPAddress.Parse(entry));
        return new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
    }
}
