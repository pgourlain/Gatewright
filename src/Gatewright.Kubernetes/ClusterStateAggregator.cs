// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using k8s.Models;

namespace Gatewright.Kubernetes;

/// <summary>Keeps the Services and EndpointSlices seen so far and computes the <see cref="DiscoveryState"/> they describe. Thread-safe.</summary>
public sealed class ClusterStateAggregator
{
    private readonly string _prefix;
    private readonly object _gate = new();
    private readonly Dictionary<string, V1Service> _services = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V1EndpointSlice> _slices = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="ClusterStateAggregator"/> class.</summary>
    /// <param name="annotationPrefix">The annotation prefix.</param>
    public ClusterStateAggregator(string annotationPrefix) => _prefix = annotationPrefix;

    /// <summary>Adds or replaces a Service.</summary>
    /// <param name="service">The Service.</param>
    public void UpsertService(V1Service service)
    {
        lock (_gate)
        {
            _services[Key(service.Metadata)] = service;
        }
    }

    /// <summary>Removes a Service.</summary>
    /// <param name="service">The Service.</param>
    public void RemoveService(V1Service service)
    {
        lock (_gate)
        {
            _services.Remove(Key(service.Metadata));
        }
    }

    /// <summary>Adds or replaces an EndpointSlice.</summary>
    /// <param name="slice">The slice.</param>
    public void UpsertSlice(V1EndpointSlice slice)
    {
        lock (_gate)
        {
            _slices[Key(slice.Metadata)] = slice;
        }
    }

    /// <summary>Removes an EndpointSlice.</summary>
    /// <param name="slice">The slice.</param>
    public void RemoveSlice(V1EndpointSlice slice)
    {
        lock (_gate)
        {
            _slices.Remove(Key(slice.Metadata));
        }
    }

    /// <summary>Forgets every Service (used before a full relist).</summary>
    public void ClearServices()
    {
        lock (_gate)
        {
            _services.Clear();
        }
    }

    /// <summary>Forgets every EndpointSlice (used before a full relist).</summary>
    public void ClearSlices()
    {
        lock (_gate)
        {
            _slices.Clear();
        }
    }

    /// <summary>Computes the current state.</summary>
    /// <returns>The services exposed and the addresses behind them.</returns>
    public DiscoveryState Snapshot()
    {
        lock (_gate)
        {
            var exposed = _services.Values.Where(s => KubernetesMapper.IsExposed(s, _prefix)).ToList();
            var addresses = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var service in exposed)
            {
                var slices = _slices.Values.Where(s =>
                    s.Metadata.NamespaceProperty == service.Metadata.NamespaceProperty
                    && s.Metadata.Labels is { } labels
                    && labels.TryGetValue(KubernetesMapper.ServiceNameLabel, out var name)
                    && name == service.Metadata.Name);
                foreach (var (reference, list) in KubernetesMapper.ToEndpoints(service, slices))
                {
                    addresses[reference] = list;
                }
            }

            return new DiscoveryState(
                [.. exposed.Select(s => KubernetesMapper.ToDiscovered(s, _prefix)).OrderBy(s => s.Namespace, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal)],
                new ServiceEndpointSet(addresses));
        }
    }

    private static string Key(V1ObjectMeta meta) => $"{meta.NamespaceProperty}/{meta.Name}";
}
