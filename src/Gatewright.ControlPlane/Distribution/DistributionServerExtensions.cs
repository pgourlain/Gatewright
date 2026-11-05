// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Distribution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Registers and maps the gRPC distribution server.</summary>
public static class DistributionServerExtensions
{
    /// <summary>Registers the distribution server services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; the <c>Distribution</c> section is bound.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddDistributionServer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<DistributionOptions>(configuration.GetSection(DistributionOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddGrpc();
        services.AddSingleton<PublicationHub>();
        services.AddHostedService(sp => sp.GetRequiredService<PublicationHub>());
        services.AddSingleton<NodeRegistry>();
        return services.AddDiscoveryHub(configuration);
    }

    /// <summary>Registers the discovery store and the hub that watches it (also used without gRPC in single-process mode).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; the <c>Distribution</c> section is bound.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddDiscoveryHub(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<DistributionOptions>(configuration.GetSection(DistributionOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DbDiscoveryStore>();
        services.TryAddSingleton<Gatewright.Core.Discovery.IDiscoveryStore>(sp => sp.GetRequiredService<DbDiscoveryStore>());
        services.TryAddSingleton<EndpointsHub>();
        services.AddHostedService(sp => sp.GetRequiredService<EndpointsHub>());
        services.Configure<TlsSecretsOptions>(configuration.GetSection(TlsSecretsOptions.SectionName));
        services.TryAddSingleton<TlsSecretsHub>();
        services.AddHostedService(sp => sp.GetRequiredService<TlsSecretsHub>());
        return services;
    }

    /// <summary>Maps the gRPC distribution endpoint.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapDistributionServer(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGrpcService<ConfigDistributionService>();
        return endpoints;
    }

    /// <summary>Registers the automatic OpenAPI import of discovered services (<c>Kubernetes:AutoImport</c>).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddOpenApiAutoImport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<AutoImportOptions>(configuration.GetSection(AutoImportOptions.SectionName));
        services.TryAddSingleton<IOpenApiFetcher, HttpOpenApiFetcher>();
        services.AddHostedService<OpenApiAutoImportService>();
        return services;
    }
}
