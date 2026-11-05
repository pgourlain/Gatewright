// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Globalization;
using Gatewright.Core.Discovery;
using k8s.Models;

namespace Gatewright.Kubernetes;

/// <summary>Turns Kubernetes objects into discovery data. Pure functions, no API calls.</summary>
public static class KubernetesMapper
{
    /// <summary>The label that ties an EndpointSlice to its Service.</summary>
    public const string ServiceNameLabel = "kubernetes.io/service-name";

    /// <summary>Tells whether a Service is exposed (<c>&lt;prefix&gt;expose: "true"</c>).</summary>
    /// <param name="service">The Service.</param>
    /// <param name="prefix">The annotation prefix.</param>
    /// <returns><see langword="true"/> when exposed.</returns>
    public static bool IsExposed(V1Service service, string prefix)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.Metadata?.Annotations is { } annotations
            && annotations.TryGetValue(prefix + "expose", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps an exposed Service to a discovered service.</summary>
    /// <param name="service">The Service.</param>
    /// <param name="prefix">The annotation prefix.</param>
    /// <returns>The discovered service.</returns>
    public static DiscoveredService ToDiscovered(V1Service service, string prefix)
    {
        ArgumentNullException.ThrowIfNull(service);
        var annotations = service.Metadata.Annotations;
        string? Get(string key) => annotations is not null && annotations.TryGetValue(prefix + key, out var value) && value.Length > 0 ? value : null;
        return new DiscoveredService(
            service.Metadata.NamespaceProperty,
            service.Metadata.Name,
            [.. (service.Spec?.Ports ?? []).Select(p => p.Port).Order()],
            Get("openapi-path"),
            Get("base-path"),
            Get("product"));
    }

    /// <summary>
    /// Computes the ready addresses behind every port of a Service from its EndpointSlices. Endpoints that are not ready, or that are
    /// terminating, are left out so that rolling updates never route to a pod that is going away.
    /// </summary>
    /// <param name="service">The Service.</param>
    /// <param name="slices">All EndpointSlices of the Service.</param>
    /// <returns>The addresses per service reference.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ToEndpoints(V1Service service, IEnumerable<V1EndpointSlice> slices)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(slices);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var sliceList = slices.ToList();
        foreach (var servicePort in service.Spec?.Ports ?? [])
        {
            var addresses = new SortedSet<string>(StringComparer.Ordinal);
            var scheme = IsHttps(servicePort) ? "https" : "http";
            foreach (var slice in sliceList)
            {
                if (!string.Equals(slice.AddressType, "IPv4", StringComparison.OrdinalIgnoreCase) && !string.Equals(slice.AddressType, "IPv6", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var slicePort = (slice.Ports ?? []).FirstOrDefault(p => string.Equals(p.Name ?? "", servicePort.Name ?? "", StringComparison.Ordinal));
                if (slicePort?.Port is not { } targetPort)
                {
                    continue;
                }

                foreach (var endpoint in slice.Endpoints ?? [])
                {
                    if (endpoint.Conditions?.Ready == false || endpoint.Conditions?.Terminating == true)
                    {
                        continue;
                    }

                    foreach (var address in endpoint.Addresses ?? [])
                    {
                        var host = address.Contains(':', StringComparison.Ordinal) ? $"[{address}]" : address;
                        addresses.Add(string.Create(CultureInfo.InvariantCulture, $"{scheme}://{host}:{targetPort}"));
                    }
                }
            }

            result[$"{service.Metadata.NamespaceProperty}/{service.Metadata.Name}:{servicePort.Port}"] = [.. addresses];
        }

        return result;
    }

    private static bool IsHttps(V1ServicePort port) =>
        string.Equals(port.AppProtocol, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(port.Name, "https", StringComparison.OrdinalIgnoreCase);
}
