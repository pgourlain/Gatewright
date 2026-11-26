// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>Keeps the <see cref="HttpRoute"/> and <see cref="Gateway"/> objects seen in the cluster and maps the routes that Gatewright serves.</summary>
public sealed class GatewayApiAggregator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HttpRoute> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Gateway> _gateways = new(StringComparer.Ordinal);
    private readonly string _annotationPrefix;
    private bool _routesListed;
    private bool _gatewaysListed;
    private readonly string _gatewayClassName;

    /// <summary>Initializes a new instance of the <see cref="GatewayApiAggregator"/> class.</summary>
    /// <param name="annotationPrefix">The annotation prefix, <c>gatewright.io/</c>.</param>
    /// <param name="gatewayClassName">The class of the Gateways whose routes are served.</param>
    public GatewayApiAggregator(string annotationPrefix, string gatewayClassName)
    {
        _annotationPrefix = annotationPrefix;
        _gatewayClassName = gatewayClassName;
    }

    /// <summary>
    /// Gets a value indicating whether both lists are complete. It turns <see langword="false"/> while a list is being rebuilt, so that a
    /// reconciliation never sees a half-filled view (and counts false absences).
    /// </summary>
    public bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return _routesListed && _gatewaysListed;
            }
        }
    }

    /// <summary>Tells that the routes are listed completely.</summary>
    public void MarkRoutesListed()
    {
        lock (_gate)
        {
            _routesListed = true;
        }
    }

    /// <summary>Tells that the Gateways are listed completely.</summary>
    public void MarkGatewaysListed()
    {
        lock (_gate)
        {
            _gatewaysListed = true;
        }
    }

    /// <summary>Records an added or modified route.</summary>
    /// <param name="route">The route.</param>
    public void UpsertRoute(HttpRoute route)
    {
        lock (_gate)
        {
            _routes[Key(route.Metadata)] = route;
        }
    }

    /// <summary>Forgets a deleted route.</summary>
    /// <param name="route">The route.</param>
    public void RemoveRoute(HttpRoute route)
    {
        lock (_gate)
        {
            _routes.Remove(Key(route.Metadata));
        }
    }

    /// <summary>Forgets every route (before a relist).</summary>
    public void ClearRoutes()
    {
        lock (_gate)
        {
            _routes.Clear();
            _routesListed = false;
        }
    }

    /// <summary>Records an added or modified Gateway.</summary>
    /// <param name="gateway">The Gateway.</param>
    public void UpsertGateway(Gateway gateway)
    {
        lock (_gate)
        {
            _gateways[Key(gateway.Metadata)] = gateway;
        }
    }

    /// <summary>Forgets a deleted Gateway.</summary>
    /// <param name="gateway">The Gateway.</param>
    public void RemoveGateway(Gateway gateway)
    {
        lock (_gate)
        {
            _gateways.Remove(Key(gateway.Metadata));
        }
    }

    /// <summary>Forgets every Gateway (before a relist).</summary>
    public void ClearGateways()
    {
        lock (_gate)
        {
            _gateways.Clear();
            _gatewaysListed = false;
        }
    }

    /// <summary>Lists the Gateways of the served class, as <c>namespace/name</c>.</summary>
    /// <returns>The Gateways.</returns>
    public IReadOnlySet<string> ServedGateways()
    {
        lock (_gate)
        {
            return _gateways
                .Where(g => string.Equals(g.Value.Spec?.GatewayClassName, _gatewayClassName, StringComparison.Ordinal))
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Maps the routes that attach to a Gateway of the served class, ordered by reference.</summary>
    /// <returns>The mapped routes.</returns>
    public IReadOnlyList<GatewayApiRoute> Snapshot()
    {
        lock (_gate)
        {
            var served = _gateways
                .Where(g => string.Equals(g.Value.Spec?.GatewayClassName, _gatewayClassName, StringComparison.Ordinal))
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
            return
            [
                .. _routes.Values
                    .Where(r => AttachesTo(r, served))
                    .Select(r => HttpRouteMapper.Map(r, _annotationPrefix))
                    .OrderBy(r => r.Ref, StringComparer.Ordinal),
            ];
        }
    }

    /// <summary>Lists the Services referenced by the routes that can be applied, as <c>namespace/name</c>.</summary>
    /// <param name="routes">The mapped routes.</param>
    /// <returns>The references.</returns>
    public static IEnumerable<string> ReferencedServices(IEnumerable<GatewayApiRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        foreach (var route in routes)
        {
            if (route.Api?.Backend.Service is { } service)
            {
                yield return service[..service.LastIndexOf(':')];
            }
        }
    }

    private static bool AttachesTo(HttpRoute route, HashSet<string> gateways)
    {
        var ns = route.Metadata.NamespaceProperty ?? "default";
        foreach (var parent in route.Spec?.ParentRefs ?? [])
        {
            if ((parent.Group is null or GatewayApiResources.Group) && (parent.Kind is null or "Gateway")
                && gateways.Contains((parent.Namespace ?? ns) + "/" + parent.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static string Key(k8s.Models.V1ObjectMeta meta) => $"{meta.NamespaceProperty ?? "default"}/{meta.Name}";
}
