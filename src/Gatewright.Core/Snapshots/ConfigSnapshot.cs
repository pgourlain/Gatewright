// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Yarp.ReverseProxy.Configuration;

namespace Gatewright.Core.Snapshots;

/// <summary>An immutable, ready-to-apply view of a configuration: YARP routes and clusters plus compiled policy pipelines.</summary>
public sealed class ConfigSnapshot
{
    /// <summary>Initializes a new instance of the <see cref="ConfigSnapshot"/> class.</summary>
    /// <param name="version">The configuration version.</param>
    /// <param name="hash">A stable hash of the configuration content.</param>
    /// <param name="routes">The YARP routes.</param>
    /// <param name="clusters">The YARP clusters.</param>
    /// <param name="pipelines">The pipeline of each route, keyed by route id.</param>
    /// <param name="security">The security data.</param>
    public ConfigSnapshot(
        long version,
        string hash,
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        IReadOnlyDictionary<string, RoutePipeline> pipelines,
        SecurityData security)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(security);
        Version = version;
        Hash = hash;
        Routes = routes;
        Clusters = clusters;
        Pipelines = pipelines;
        Security = security;
    }

    /// <summary>Gets the configuration version.</summary>
    public long Version { get; }

    /// <summary>Gets the hash of the configuration content (excluding the version).</summary>
    public string Hash { get; }

    /// <summary>Gets the YARP routes.</summary>
    public IReadOnlyList<RouteConfig> Routes { get; }

    /// <summary>Gets the YARP clusters.</summary>
    public IReadOnlyList<ClusterConfig> Clusters { get; }

    /// <summary>Gets the pipeline of each route, keyed by route id.</summary>
    public IReadOnlyDictionary<string, RoutePipeline> Pipelines { get; }

    /// <summary>Gets the security data.</summary>
    public SecurityData Security { get; }
}
