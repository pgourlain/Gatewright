// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Gatewright.Distribution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Configuration;

namespace Gatewright.DataPlane;

/// <summary>Registers the data plane services.</summary>
public static class DataPlaneServiceCollectionExtensions
{
    /// <summary>Registers YARP backed by an in-memory provider, the pipeline registry and the snapshot applier.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGatewrightDataPlane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddReverseProxy();
        services.AddSingleton(new InMemoryConfigProvider([], []));
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryConfigProvider>());
        services.AddSingleton<PipelineRegistry>();
        services.AddSingleton<SnapshotApplier>();
        services.AddOptions<Gatewright.DataPlane.Telemetry.TelemetryOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Gatewright.DataPlane.Telemetry.AccessLogService>();
        services.AddHostedService(sp => sp.GetRequiredService<Gatewright.DataPlane.Telemetry.AccessLogService>());
        services.AddSingleton<Gatewright.DataPlane.Telemetry.IUsageSink, Gatewright.DataPlane.Telemetry.MetricsUsageSink>();
        services.AddSingleton<Gatewright.DataPlane.Telemetry.UsageAggregator>();
        services.AddHostedService(sp => sp.GetRequiredService<Gatewright.DataPlane.Telemetry.UsageAggregator>());
        services.AddSingleton<DataPlaneMetrics>();
        services.AddSingleton<ReadinessGate>();
        services.AddHostedService(sp => sp.GetRequiredService<ReadinessGate>());
        services.AddHostedService(sp => sp.GetRequiredService<DataPlaneMetrics>());
        services.TryAddSingleton<IGrpcChannelProvider, DefaultGrpcChannelProvider>();
        services.TryAddSingleton<IRevocationFeed, InMemoryRevocationFeed>();
        services.AddHostedService<RevocationListenerService>();
        return services;
    }

    /// <summary>Registers the client that receives configuration from a control plane over gRPC.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; the <c>Distribution</c> section is bound.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="InvalidOperationException"><c>Distribution:Endpoint</c> or <c>Distribution:Token</c> is missing.</exception>
    public static IServiceCollection AddDistributionClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(DistributionOptions.SectionName);
        if (string.IsNullOrWhiteSpace(section[nameof(DistributionOptions.Endpoint)]) || string.IsNullOrWhiteSpace(section[nameof(DistributionOptions.Token)]))
        {
            throw new InvalidOperationException("Data plane mode requires Distribution:Endpoint and Distribution:Token.");
        }

        services.Configure<DistributionOptions>(section);
        services.AddHostedService<GrpcConfigClientService>();
        return services;
    }
}
