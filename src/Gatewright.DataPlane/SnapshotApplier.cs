// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using Gatewright.Core.Security;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Tls;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Gatewright.DataPlane;

/// <summary>
/// Applies snapshots to YARP and to the pipeline registry. A snapshot rejected by YARP validation never replaces the
/// last known good configuration.
/// </summary>
public sealed partial class SnapshotApplier
{
    private readonly InMemoryConfigProvider _provider;
    private readonly IConfigValidator _validator;
    private readonly PipelineRegistry _registry;
    private readonly IReadOnlyList<ISecurityDataSink> _securitySinks;
    private readonly CertificateStore? _certificateStore;
    private readonly ILogger<SnapshotApplier> _logger;
    private readonly Lock _gate = new();
    private ServiceEndpointSet _endpoints = ServiceEndpointSet.Empty;
    private ConfigSnapshot? _applied;

    /// <summary>Initializes a new instance of the <see cref="SnapshotApplier"/> class.</summary>
    /// <param name="provider">The YARP in-memory configuration provider.</param>
    /// <param name="validator">The YARP configuration validator.</param>
    /// <param name="registry">The pipeline registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="securitySinks">Receivers of the snapshot's security data.</param>
    /// <param name="certificateStore">Receives pushed TLS secret material; <see langword="null"/> when TLS termination is not wired.</param>
    public SnapshotApplier(
        InMemoryConfigProvider provider,
        IConfigValidator validator,
        PipelineRegistry registry,
        ILogger<SnapshotApplier> logger,
        IEnumerable<ISecurityDataSink>? securitySinks = null,
        CertificateStore? certificateStore = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _validator = validator;
        _registry = registry;
        _logger = logger;
        _securitySinks = [.. securitySinks ?? []];
        _certificateStore = certificateStore;
    }

    /// <summary>Validates then applies a snapshot.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns><see langword="true"/> when applied; <see langword="false"/> when rejected and the previous configuration is kept.</returns>
    public async ValueTask<bool> ApplyAsync(ConfigSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<Exception>();
        var clusters = WithEndpoints(snapshot, Volatile.Read(ref _endpoints));
        foreach (var route in snapshot.Routes)
        {
            errors.AddRange(await _validator.ValidateRouteAsync(route).ConfigureAwait(false));
        }

        foreach (var cluster in clusters)
        {
            errors.AddRange(await _validator.ValidateClusterAsync(cluster).ConfigureAwait(false));
        }

        if (errors.Count > 0)
        {
            LogRejected(_logger, snapshot.Version, string.Join("; ", errors.Select(e => e.Message)));
            return false;
        }

        lock (_gate)
        {
            foreach (var sink in _securitySinks)
            {
                sink.Apply(snapshot.Security);
            }

            // Pipelines first: a request matched by the new YARP config must find its pipeline.
            _registry.Swap(snapshot);
            _applied = snapshot;
            _provider.Update(snapshot.Routes, WithEndpoints(snapshot, _endpoints));
        }

        LogApplied(_logger, snapshot.Version, snapshot.Hash, snapshot.Routes.Count);
        return true;
    }

    /// <summary>
    /// Replaces the addresses behind discovered services (pods scaled, restarted…) without changing the configuration version.
    /// Clusters that reference a service get these addresses as destinations.
    /// </summary>
    /// <param name="endpoints">The addresses per service reference.</param>
    public void ApplyEndpoints(ServiceEndpointSet endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        lock (_gate)
        {
            _endpoints = endpoints;
            if (_applied is { } snapshot)
            {
                _provider.Update(snapshot.Routes, WithEndpoints(snapshot, endpoints));
            }
        }

        LogEndpoints(_logger, endpoints.Addresses.Count);
    }

    /// <summary>Applies the certificate material of a Kubernetes-secret-sourced hostname, pushed by the control plane outside any configuration version.</summary>
    /// <param name="reference">The secret reference, <c>namespace/name</c>.</param>
    /// <param name="certificatePem">The certificate chain, PEM encoded.</param>
    /// <param name="keyPem">The private key, PEM encoded.</param>
    public void ApplySecret(string reference, string certificatePem, string keyPem) =>
        _certificateStore?.SetSecret(reference, certificatePem, keyPem);

    private static List<ClusterConfig> WithEndpoints(ConfigSnapshot snapshot, ServiceEndpointSet endpoints)
    {
        var clusters = new List<ClusterConfig>(snapshot.Clusters.Count);
        foreach (var cluster in snapshot.Clusters)
        {
            if (cluster.Metadata is { } metadata && metadata.TryGetValue(SnapshotTranslator.ServiceMetadataKey, out var reference))
            {
                var destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal);
                var addresses = endpoints.For(reference);
                for (var i = 0; i < addresses.Count; i++)
                {
                    destinations[$"d{i}"] = new DestinationConfig { Address = addresses[i] };
                }

                clusters.Add(cluster with { Destinations = destinations });
            }
            else
            {
                clusters.Add(cluster);
            }
        }

        return clusters;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated discovered endpoints ({Services} services).")]
    private static partial void LogEndpoints(ILogger logger, int services);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied configuration version {Version} (hash {Hash}, {Routes} routes).")]
    private static partial void LogApplied(ILogger logger, long version, string hash, int routes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Configuration version {Version} rejected by YARP, keeping the previous one: {Errors}")]
    private static partial void LogRejected(ILogger logger, long version, string errors);
}
