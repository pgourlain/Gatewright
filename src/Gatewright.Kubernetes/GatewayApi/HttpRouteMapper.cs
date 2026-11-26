// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Globalization;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>
/// Maps an <see cref="HttpRoute"/> to the API it asks for (ADR 0017). A feature that cannot be mapped is reported, never silently ignored:
/// the route is then not applied at all.
/// </summary>
public static class HttpRouteMapper
{
    private static readonly string[] AnyMethod = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private static readonly HashSet<string> SupportedMethods = new(AnyMethod.Append("TRACE"), StringComparer.Ordinal);

    /// <summary>Builds the name of the API of a route.</summary>
    /// <param name="namespaceName">The namespace of the route.</param>
    /// <param name="name">The name of the route.</param>
    /// <returns>The API name, such as <c>shop.orders</c>.</returns>
    public static string ApiName(string namespaceName, string name) => namespaceName + "." + name;

    /// <summary>Maps a route.</summary>
    /// <param name="route">The route.</param>
    /// <param name="annotationPrefix">The annotation prefix, <c>gatewright.io/</c>.</param>
    /// <returns>The mapped route; its <see cref="GatewayApiRoute.Api"/> is <see langword="null"/> when a problem was found.</returns>
    public static GatewayApiRoute Map(HttpRoute route, string annotationPrefix)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(annotationPrefix);

        var ns = route.Metadata.NamespaceProperty ?? "default";
        var name = route.Metadata.Name ?? "";
        var reference = ns + "/" + name;
        var problems = new List<string>();
        var matches = new List<RouteMatch>();
        var backends = new SortedSet<string>(StringComparer.Ordinal);

        var rules = route.Spec?.Rules ?? [];
        if (rules.Count == 0)
        {
            problems.Add("The route has no rule.");
        }

        for (var r = 0; r < rules.Count; r++)
        {
            var rule = rules[r];
            if (rule.Filters is { Count: > 0 })
            {
                problems.Add($"rules[{r}]: filters are not supported.");
            }

            if (rule.Timeouts is not null)
            {
                problems.Add($"rules[{r}]: timeouts are not supported.");
            }

            var ruleMatches = rule.Matches is { Count: > 0 } ? rule.Matches : [new HttpRouteMatch()];
            for (var m = 0; m < ruleMatches.Count; m++)
            {
                AddMatch(matches, problems, ruleMatches[m], $"rules[{r}].matches[{m}]", r, m);
            }

            foreach (var backend in rule.BackendRefs ?? [])
            {
                AddBackend(backends, problems, backend, ns, $"rules[{r}].backendRefs");
            }
        }

        if (backends.Count == 0 && problems.Count == 0)
        {
            problems.Add("The route has no backend.");
        }
        else if (backends.Count > 1)
        {
            problems.Add($"Several distinct backends are not supported ({string.Join(", ", backends)}).");
        }

        var annotations = route.Metadata.Annotations;
        var product = annotations is not null && annotations.TryGetValue(annotationPrefix + "product", out var value) && value.Length > 0 ? value : null;
        var uid = route.Metadata.Uid ?? "";
        if (problems.Count > 0)
        {
            return new GatewayApiRoute(reference, uid, route.Metadata.Generation, null, problems);
        }

        var api = Build(ns, name, reference, backends.First(), route.Spec!.Hostnames, matches, product);
        return new GatewayApiRoute(reference, uid, route.Metadata.Generation, api, []);
    }

    private static void AddMatch(List<RouteMatch> matches, List<string> problems, HttpRouteMatch match, string where, int rule, int index)
    {
        if (match.Headers is { Count: > 0 } || match.QueryParams is { Count: > 0 })
        {
            problems.Add($"{where}: header and query parameter matches are not supported.");
            return;
        }

        var type = match.Path?.Type ?? "PathPrefix";
        var value = match.Path?.Value ?? "/";
        if (type is not ("Exact" or "PathPrefix"))
        {
            problems.Add($"{where}: path type '{type}' is not supported.");
            return;
        }

        if (value.Length == 0 || value[0] != '/')
        {
            problems.Add($"{where}: the path must start with '/'.");
            return;
        }

        string? method = null;
        if (!string.IsNullOrEmpty(match.Method))
        {
            method = match.Method.ToUpperInvariant();
            if (!SupportedMethods.Contains(method))
            {
                problems.Add($"{where}: method '{match.Method}' is not supported.");
                return;
            }
        }

        matches.Add(new RouteMatch(type == "Exact", value.Split('/', StringSplitOptions.RemoveEmptyEntries), method, rule, index));
    }

    private static void AddBackend(SortedSet<string> backends, List<string> problems, BackendRef backend, string routeNamespace, string where)
    {
        if (!string.IsNullOrEmpty(backend.Group) || !(backend.Kind is null or "Service"))
        {
            problems.Add($"{where}: only Service backends are supported.");
            return;
        }

        if (string.IsNullOrEmpty(backend.Name) || backend.Port is not > 0)
        {
            problems.Add($"{where}: a backend needs a name and a port.");
            return;
        }

        if (!string.IsNullOrEmpty(backend.Namespace) && backend.Namespace != routeNamespace)
        {
            problems.Add($"{where}: a backend in another namespace is not supported.");
            return;
        }

        if (backend.Filters is { Count: > 0 })
        {
            problems.Add($"{where}: backend filters are not supported.");
            return;
        }

        if (backend.Weight == 0)
        {
            problems.Add($"{where}: a weight of 0 is not supported.");
            return;
        }

        backends.Add($"{routeNamespace}/{backend.Name}:{backend.Port.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static ApiDefinition Build(string ns, string name, string reference, string service, List<string>? hostnames, List<RouteMatch> matches, string? product)
    {
        var baseSegments = CommonPrefix(matches);
        var basePath = baseSegments.Length == 0 ? "/" : "/" + string.Join('/', baseSegments);
        var operations = new List<OperationDefinition>();

        // The common case, one prefix and no method, needs no operation: the API is a catch-all under its base path.
        if (!(matches.Count == 1 && !matches[0].Exact && matches[0].Method is null))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                var relative = match.Segments.Skip(baseSegments.Length).ToArray();
                var path = match.Exact
                    ? "/" + string.Join('/', relative)
                    : relative.Length == 0 ? "/{**rest}" : "/" + string.Join('/', relative) + "/{**rest}";
                foreach (var method in match.Method is null ? AnyMethod : [match.Method])
                {
                    if (seen.Add(method + " " + path))
                    {
                        var suffix = match.Method is null ? "-" + method.ToLowerInvariant() : "";
                        operations.Add(new OperationDefinition { Name = $"r{match.Rule}m{match.Index}{suffix}", Method = method, Path = path });
                    }
                }
            }
        }

        return new ApiDefinition
        {
            Name = ApiName(ns, name),
            BasePath = basePath,
            Hosts = hostnames is { Count: > 0 } ? [.. hostnames] : null,
            Backend = new BackendDefinition { Service = service },
            Operations = operations,
            Source = new ApiSource { Kind = ApiSource.GatewayApi, Ref = reference, Product = product },
        };
    }

    // An exact match keeps its last segment out of the base path, so that "/orders/1" becomes the operation "/1" of the base "/orders".
    private static string[] CommonPrefix(List<RouteMatch> matches)
    {
        string[]? common = null;
        foreach (var match in matches)
        {
            var segments = match.Exact && match.Segments.Length > 0 ? match.Segments[..^1] : match.Segments;
            if (common is null)
            {
                common = segments;
                continue;
            }

            var length = 0;
            while (length < common.Length && length < segments.Length && common[length] == segments[length])
            {
                length++;
            }

            common = common[..length];
        }

        return common ?? [];
    }

    private sealed record RouteMatch(bool Exact, string[] Segments, string? Method, int Rule, int Index);
}
