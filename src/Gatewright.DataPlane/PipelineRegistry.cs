// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Snapshots;

namespace Gatewright.DataPlane;

/// <summary>
/// Gives the request path access to the policy pipelines of the applied snapshot. The reference is swapped atomically;
/// the previous snapshot stays reachable for requests matched by YARP just before a swap.
/// </summary>
public sealed class PipelineRegistry
{
    private Generation? _generation;

    /// <summary>Gets the snapshot currently applied, or <see langword="null"/> before the first one.</summary>
    public ConfigSnapshot? Current => Volatile.Read(ref _generation)?.Current;

    /// <summary>Gets a value indicating whether a snapshot has been applied.</summary>
    public bool HasSnapshot => Current is not null;

    /// <summary>Atomically replaces the applied snapshot.</summary>
    /// <param name="snapshot">The new snapshot.</param>
    public void Swap(ConfigSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var previous = Volatile.Read(ref _generation);
        Volatile.Write(ref _generation, new Generation(snapshot, previous?.Current));
    }

    /// <summary>Finds the pipeline of a route, looking at the current then the previous snapshot.</summary>
    /// <param name="routeId">The YARP route id.</param>
    /// <param name="pipeline">The pipeline, when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGetPipeline(string routeId, out RoutePipeline pipeline)
    {
        var generation = Volatile.Read(ref _generation);
        if (generation is not null)
        {
            if (generation.Current.Pipelines.TryGetValue(routeId, out pipeline!))
            {
                return true;
            }

            if (generation.Previous is not null && generation.Previous.Pipelines.TryGetValue(routeId, out pipeline!))
            {
                return true;
            }
        }

        pipeline = null!;
        return false;
    }

    private sealed record Generation(ConfigSnapshot Current, ConfigSnapshot? Previous);
}
