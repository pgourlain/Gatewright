// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;
using Gatewright.Core.Discovery;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>
/// Writes the outcome of a synchronization to <c>status.parents</c> of the routes (ADR 0017). Only the entries that carry
/// <see cref="ControllerName"/> are replaced, those of other controllers are kept. A route is patched only when its entry changed.
/// </summary>
public sealed partial class RouteStatusWriter
{
    /// <summary>The controller name written in <c>status.parents[].controllerName</c>.</summary>
    public const string ControllerName = "gatewright.io/gateway-controller";

    /// <summary>The condition that tells whether the API of an accepted route serves or is blocked.</summary>
    public const string ConfiguredCondition = "gatewright.io/Configured";

    private readonly IKubernetes _client;
    private readonly TimeProvider _time;
    private readonly ILogger<RouteStatusWriter> _logger;

    /// <summary>Initializes a new instance of the <see cref="RouteStatusWriter"/> class.</summary>
    /// <param name="client">The Kubernetes client.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public RouteStatusWriter(IKubernetes client, TimeProvider time, ILogger<RouteStatusWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _time = time;
        _logger = logger;
    }

    /// <summary>Writes the results to the routes.</summary>
    /// <param name="results">One result per route.</param>
    /// <param name="servedGateways">The Gateways of the served class, as <c>namespace/name</c>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completing when written.</returns>
    public async Task WriteAsync(IReadOnlyList<GatewayApiRouteResult> results, IReadOnlySet<string> servedGateways, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(servedGateways);
        foreach (var result in results)
        {
            var slash = result.Ref.IndexOf('/', StringComparison.Ordinal);
            var (ns, name) = (result.Ref[..slash], result.Ref[(slash + 1)..]);
            try
            {
                var current = await _client.CustomObjects.GetNamespacedCustomObjectAsync(GatewayApiResources.Group, GatewayApiResources.Version, ns, GatewayApiResources.HttpRoutes, name, cancellationToken: cancellationToken).ConfigureAwait(false);
                var route = JsonNode.Parse(current is JsonElement element ? element.GetRawText() : KubernetesJson.Serialize(current))!;
                if (BuildParents(route, servedGateways, result, _time.GetUtcNow()) is not { } parents)
                {
                    continue;
                }

                var patch = new JsonObject { ["status"] = new JsonObject { ["parents"] = parents } }.ToJsonString();
                await _client.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                    new V1Patch(patch, V1Patch.PatchType.MergePatch),
                    GatewayApiResources.Group,
                    GatewayApiResources.Version,
                    ns,
                    GatewayApiResources.HttpRoutes,
                    name,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A status that cannot be written (route gone, right missing) never stops the synchronization.
                LogFailed(_logger, ex, result.Ref);
            }
        }
    }

    /// <summary>Builds the new <c>status.parents</c> of a route, or <see langword="null"/> when nothing has to change.</summary>
    /// <param name="route">The route as read from the API server.</param>
    /// <param name="servedGateways">The Gateways of the served class, as <c>namespace/name</c>.</param>
    /// <param name="result">The outcome of the synchronization.</param>
    /// <param name="now">The current time, used when a condition changes status.</param>
    /// <returns>The complete array to patch, or <see langword="null"/>.</returns>
    public static JsonArray? BuildParents(JsonNode route, IReadOnlySet<string> servedGateways, GatewayApiRouteResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(servedGateways);
        ArgumentNullException.ThrowIfNull(result);

        var ns = route["metadata"]?["namespace"]?.GetValue<string>() ?? "default";
        var generation = route["metadata"]?["generation"]?.GetValue<long>() ?? 0;
        var existing = route["status"]?["parents"] as JsonArray ?? [];
        var kept = new JsonArray();
        var previous = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var entry in existing.OfType<JsonObject>())
        {
            if (entry["controllerName"]?.GetValue<string>() == ControllerName)
            {
                previous[ParentKey(entry["parentRef"], ns)] = entry;
            }
            else
            {
                kept.Add(JsonNode.Parse(entry.ToJsonString()));
            }
        }

        var parents = new JsonArray();
        var changed = false;
        foreach (var parentRef in (route["spec"]?["parentRefs"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var group = parentRef["group"]?.GetValue<string>();
            var kind = parentRef["kind"]?.GetValue<string>();
            var key = ParentKey(parentRef, ns);
            if (!(group is null or GatewayApiResources.Group) || !(kind is null or "Gateway") || !servedGateways.Contains(key))
            {
                continue;
            }

            previous.TryGetValue(key, out var before);
            var conditions = new JsonArray
            {
                Condition("Accepted", result.Accepted, result.Reason, result.Message, generation, before, now),
                Condition(
                    ConfiguredCondition,
                    result.Accepted && result.BlockedReason is null,
                    result.BlockedReason ?? (result.Accepted ? "Configured" : result.Reason),
                    result.BlockedReason is null ? (result.Accepted ? "The API serves traffic." : "The route is not applied.") : $"The API answers 503 until it is configured completely ({result.BlockedReason}): add the {"gatewright.io/product"} annotation with a product that has policies.",
                    generation,
                    before,
                    now),
            };
            var entry = new JsonObject
            {
                ["parentRef"] = JsonNode.Parse(parentRef.ToJsonString()),
                ["controllerName"] = ControllerName,
                ["conditions"] = conditions,
            };
            changed |= before is null || !Same(before["conditions"] as JsonArray, conditions);
            parents.Add(entry);
        }

        changed |= previous.Count != parents.Count;
        if (!changed)
        {
            return null;
        }

        foreach (var entry in parents)
        {
            kept.Add(entry!.DeepClone());
        }

        return kept;
    }

    private static string ParentKey(JsonNode? parentRef, string routeNamespace) =>
        (parentRef?["namespace"]?.GetValue<string>() ?? routeNamespace) + "/" + parentRef?["name"]?.GetValue<string>();

    private static JsonObject Condition(string type, bool status, string reason, string message, long generation, JsonObject? before, DateTimeOffset now)
    {
        var wanted = status ? "True" : "False";
        var earlier = (before?["conditions"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(c => c["type"]?.GetValue<string>() == type);
        var transition = earlier?["status"]?.GetValue<string>() == wanted && earlier["lastTransitionTime"] is { } time
            ? time.GetValue<string>()
            : now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["type"] = type,
            ["status"] = wanted,
            ["reason"] = reason,
            ["message"] = message,
            ["observedGeneration"] = generation,
            ["lastTransitionTime"] = transition,
        };
    }

    private static bool Same(JsonArray? left, JsonArray right)
    {
        if (left is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < right.Count; i++)
        {
            foreach (var field in new[] { "type", "status", "reason", "message", "observedGeneration", "lastTransitionTime" })
            {
                if (left[i]?[field]?.ToJsonString() != right[i]![field]!.ToJsonString())
                {
                    return false;
                }
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the status of route {Route}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string route);
}
