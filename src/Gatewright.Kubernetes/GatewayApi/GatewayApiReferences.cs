// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>
/// The Services referenced by the routes that are served. Their endpoints are discovered even though the Service carries no
/// <c>gatewright.io/expose</c> annotation.
/// </summary>
public sealed class GatewayApiReferences
{
    private volatile HashSet<string> _services = new(StringComparer.Ordinal);

    /// <summary>Raised when the set of referenced Services changes.</summary>
    public event Action? Changed;

    /// <summary>Tells whether a Service is referenced by a served route.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="name">The Service name.</param>
    /// <returns><see langword="true"/> when referenced.</returns>
    public bool Contains(string namespaceName, string name) => _services.Contains(namespaceName + "/" + name);

    /// <summary>Replaces the referenced Services.</summary>
    /// <param name="services">The references, as <c>namespace/name</c>.</param>
    public void Set(IEnumerable<string> services)
    {
        var next = new HashSet<string>(services, StringComparer.Ordinal);
        if (_services.SetEquals(next))
        {
            return;
        }

        _services = next;
        Changed?.Invoke();
    }
}
