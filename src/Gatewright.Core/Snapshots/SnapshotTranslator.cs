// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Validation;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;

namespace Gatewright.Core.Snapshots;

/// <summary>
/// Validates a <see cref="GatewayConfig"/>, compiles its policies and translates it to a <see cref="ConfigSnapshot"/>.
/// One YARP route is produced per operation, or per API when it declares no operation.
/// </summary>
public sealed class SnapshotTranslator
{
    /// <summary>Route metadata key holding the API name.</summary>
    public const string ApiMetadataKey = "gw.api";

    /// <summary>Route metadata key holding the operation name.</summary>
    public const string OperationMetadataKey = "gw.operation";

    /// <summary>Cluster metadata key holding the discovered service reference.</summary>
    public const string ServiceMetadataKey = "gw.service";

    private readonly GatewayConfigValidator _validator;
    private readonly PolicyCompiler _compiler;
    private readonly PolicyPipelineOptions _pipelineOptions;

    /// <summary>Initializes a new instance of the <see cref="SnapshotTranslator"/> class.</summary>
    /// <param name="validator">The structural validator.</param>
    /// <param name="compiler">The policy compiler.</param>
    /// <param name="pipelineOptions">Options passed to the pipelines.</param>
    public SnapshotTranslator(GatewayConfigValidator validator, PolicyCompiler compiler, PolicyPipelineOptions? pipelineOptions = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(compiler);
        _validator = validator;
        _compiler = compiler;
        _pipelineOptions = pipelineOptions ?? new PolicyPipelineOptions();
    }

    /// <summary>Translates a configuration.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The snapshot, or every error found.</returns>
    public SnapshotTranslationResult Translate(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = _validator.Validate(config).Select(e => e.ToString()).ToList();
        if (errors.Count > 0)
        {
            return new SnapshotTranslationResult(null, errors);
        }

        var compiled = _compiler.Compile(config);
        if (!compiled.IsSuccess)
        {
            return new SnapshotTranslationResult(null, [.. compiled.Errors.Select(e => e.ToString())]);
        }

        var routes = new List<RouteConfig>();
        var pipelines = new Dictionary<string, RoutePipeline>(StringComparer.Ordinal);
        var compiledByKey = compiled.Operations.ToDictionary(o => (o.Api, o.Operation));

        foreach (var api in config.Apis)
        {
            var apiInfo = new ApiInfo(api.Name, api.BasePath, api.Version);
            var basePath = TrimBasePath(api.BasePath);

            if (api.Operations.Count == 0)
            {
                AddRoute(routes, pipelines, api, null, apiInfo, null, $"{basePath}/{{**remainder}}", null, compiledByKey[(api.Name, null)]);
                continue;
            }

            foreach (var operation in api.Operations)
            {
                var opInfo = new OperationInfo(operation.Name, operation.Method.ToUpperInvariant(), operation.Path);
                var path = operation.Path == "/" ? (basePath.Length == 0 ? "/" : basePath) : basePath + operation.Path.TrimEnd('/');
                AddRoute(routes, pipelines, api, operation, apiInfo, opInfo, path, [opInfo.Method], compiledByKey[(api.Name, operation.Name)]);
            }
        }

        var clusters = config.Apis.Select(CreateCluster).ToList();
        var snapshot = new ConfigSnapshot(config.Version, ConfigHasher.Hash(config), routes, clusters, pipelines, new SecurityData { Subscriptions = config.Subscriptions, Hostnames = config.Hostnames });
        return new SnapshotTranslationResult(snapshot, []);
    }

    private static string TrimBasePath(string basePath) => basePath.TrimEnd('/');

    private void AddRoute(
        List<RouteConfig> routes,
        Dictionary<string, RoutePipeline> pipelines,
        ApiDefinition api,
        OperationDefinition? operation,
        ApiInfo apiInfo,
        OperationInfo? opInfo,
        string path,
        IReadOnlyList<string>? methods,
        CompiledOperation compiled)
    {
        var routeId = operation is null ? api.Name : $"{api.Name}:{operation.Name}";
        var metadata = new Dictionary<string, string> { [ApiMetadataKey] = api.Name };
        if (operation is not null)
        {
            metadata[OperationMetadataKey] = operation.Name;
        }

        var transforms = new List<IReadOnlyDictionary<string, string>>();
        var prefix = TrimBasePath(api.BasePath);
        if (prefix.Length > 0)
        {
            transforms.Add(new Dictionary<string, string> { ["PathRemovePrefix"] = prefix });
        }

        routes.Add(new RouteConfig
        {
            RouteId = routeId,
            ClusterId = api.Name,
            Match = new RouteMatch { Path = path, Methods = methods },
            Metadata = metadata,
            Transforms = transforms,
        });

        pipelines[routeId] = new RoutePipeline(routeId, apiInfo, opInfo, new PolicyPipeline(compiled.Chains, _pipelineOptions));
    }

    private static HealthCheckConfig? CreateHealthCheck(BackendDefinition backend)
    {
        var definition = backend.HealthCheck ?? (backend.Service is null ? null : new HealthCheckDefinition { Passive = new PassiveHealthCheckDefinition() });
        if (definition is null)
        {
            return null;
        }

        return new HealthCheckConfig
        {
            Active = definition.Active is { Enabled: true } active
                ? new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = active.Interval,
                    Timeout = active.Timeout,
                    Path = active.Path,
                    Policy = HealthCheckConstants.ActivePolicy.ConsecutiveFailures,
                }
                : null,
            Passive = definition.Passive is { Enabled: true } passive
                ? new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    ReactivationPeriod = passive.ReactivationPeriod,
                    Policy = HealthCheckConstants.PassivePolicy.TransportFailureRate,
                }
                : null,
            AvailableDestinationsPolicy = HealthCheckConstants.AvailableDestinations.HealthyOrPanic,
        };
    }

    private static ClusterConfig CreateCluster(ApiDefinition api)
    {
        var backend = api.Backend;
        var destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal);
        for (var i = 0; i < backend.Destinations.Count; i++)
        {
            destinations[$"d{i}"] = new DestinationConfig { Address = backend.Destinations[i] };
        }

        var metadata = new Dictionary<string, string>();
        if (backend.Service is not null)
        {
            metadata[ServiceMetadataKey] = backend.Service;
        }

        return new ClusterConfig
        {
            ClusterId = api.Name,
            Destinations = destinations,
            Metadata = metadata,
            HealthCheck = CreateHealthCheck(backend),
            HttpRequest = backend.Timeout is null && backend.HttpVersion is null
                ? null
                : new ForwarderRequestConfig
                {
                    ActivityTimeout = backend.Timeout,
                    Version = backend.HttpVersion is null ? null : Version.Parse(backend.HttpVersion == "2" ? "2.0" : backend.HttpVersion),
                    VersionPolicy = backend.HttpVersion is null ? null : HttpVersionPolicy.RequestVersionExact,
                },
        };
    }
}
