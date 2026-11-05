// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Distribution;
using Gatewright.Core;
using Gatewright.Core.Distribution;
using Gatewright.Core.Hosting;
using Gatewright.Core.Security;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Sources;
using Gatewright.DataPlane;
using Gatewright.DataPlane.Tls;
using Gatewright.Distribution;
using Gatewright.Kubernetes;
using Gatewright.Policies;
using Gatewright.State.Redis;
using Microsoft.Extensions.Options;

namespace Gatewright.Sdk.Internal;

/// <summary>Registers the Gatewright components according to <see cref="GatewayOptions.Mode"/>.</summary>
internal static class GatewrightServiceCollectionExtensions
{
    /// <summary>Binds and validates <see cref="GatewayOptions"/> and registers the components of the selected mode.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGatewright(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>();
        services.AddOptions<GatewayOptions>()
            .Bind(configuration.GetSection(GatewayOptions.SectionName))
            .ValidateOnStart();

        // Bind eagerly so that an invalid mode fails the host construction with an explicit message.
        var options = new GatewayOptions();
        configuration.GetSection(GatewayOptions.SectionName).Bind(options);
        var validation = new GatewayOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions), validation.Failures);
        }

        services.AddGatewrightCore();
        services.AddSingleton(new ApiKeyHasher(options.ApiKeyPepper));
        services.AddBuiltInPolicies();

        switch (options.Mode)
        {
            case GatewayMode.All:
                services.AddControlPlane().AddDataPlane().AddDistribution(options.Mode).AddStateStore(configuration);
                AddDataPlaneNetworking(services, configuration);
                if (options.ConfigSource == ConfigSourceKind.Database)
                {
                    services.AddControlPlaneDatabase(configuration, environment);
                    services.AddDiscoveryHub(configuration);
                    if (services.AddKubernetesDiscoveryIfEnabled(configuration))
                    {
                        services.AddHostedService<LocalEndpointsApplierService>();
                        services.AddHostedService<LocalSecretsApplierService>();
                        services.AddOpenApiAutoImport(configuration);
                    }

                    services.AddHostedService(sp => new PublicationWatcherService(
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        sp.GetRequiredService<SnapshotTranslator>(),
                        sp.GetRequiredService<IConfigDistributor>(),
                        options.Environment,
                        options.ConfigPollInterval,
                        sp.GetRequiredService<ILogger<PublicationWatcherService>>()));
                }
                else
                {
                    services.AddSingleton<IConfigSource>(sp => new FileConfigSource(
                        options.ConfigFile!,
                        options.ConfigPollInterval,
                        sp.GetRequiredService<ILogger<FileConfigSource>>()));
                    services.AddHostedService<ConfigPublisherService>();
                }

                services.AddHostedService<SnapshotSubscriptionService>();
                break;
            case GatewayMode.ControlPlane:
                services.AddControlPlane().AddDistribution(options.Mode);
                services.AddControlPlaneDatabase(configuration, environment);
                services.AddDistributionServer(configuration);
                if (services.AddKubernetesDiscoveryIfEnabled(configuration))
                {
                    services.AddOpenApiAutoImport(configuration);
                }

                break;
            case GatewayMode.DataPlane:
                services.AddDataPlane().AddDistribution(options.Mode).AddStateStore(configuration);
                AddDataPlaneNetworking(services, configuration);
                services.AddDistributionClient(configuration);
                break;
        }

        return services;
    }

    /// <summary>Registers the control plane components.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddControlPlane(this IServiceCollection services) => AddComponent(services, "control-plane");

    /// <summary>Registers the data plane components.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddDataPlane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGatewrightDataPlane();
        return AddComponent(services, "data-plane");
    }

    private static void AddDataPlaneNetworking(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGatewrightTls(configuration);
        services.Configure<ShutdownOptions>(configuration.GetSection(ShutdownOptions.SectionName));
        if (services.AddGatewrightForwardedHeaders(configuration))
        {
            AddComponent(services, "forwarded-headers");
        }
    }

    /// <summary>Registers the configuration distribution matching the mode: in-process for <see cref="GatewayMode.All"/>, gRPC otherwise.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="mode">The process role.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddDistribution(this IServiceCollection services, GatewayMode mode)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (mode == GatewayMode.All)
        {
            services.AddInProcessDistribution();
        }

        return AddComponent(
        services,
        mode switch
        {
            GatewayMode.All => "distribution:in-process",
            GatewayMode.ControlPlane => "distribution:grpc-server",
            GatewayMode.DataPlane => "distribution:grpc-client",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown gateway mode."),
        });
    }

    /// <summary>Registers the shared state stores: Redis when <c>Redis:ConnectionString</c> is set, in-memory otherwise.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStateStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddComponent(services, services.AddRedisStateIfConfigured(configuration) ? "state-store:redis" : "state-store:memory");
    }

    private static IServiceCollection AddControlPlaneDatabase(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<ControlPlaneOptions>().Bind(configuration.GetSection(ControlPlaneOptions.SectionName));
        var controlPlane = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(controlPlane);
        switch (controlPlane.Provider)
        {
            case DatabaseProvider.Postgres:
                services.AddControlPlanePostgres(controlPlane.ConnectionString);
                break;
            case DatabaseProvider.Sqlite:
            default:
                services.AddControlPlaneSqlite(controlPlane.ConnectionString);
                break;
        }

        services.AddHostedService<DatabaseInitializer>();
        services.AddAdminApi(configuration, environment);
        return AddComponent(services, "admin-api");
    }

    private static IServiceCollection AddComponent(IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        var component = new GatewayComponent(name);
        if (!services.Any(d => d.ServiceType == typeof(GatewayComponent) && Equals(d.ImplementationInstance, component)))
        {
            services.AddSingleton(component);
        }

        return services;
    }
}
