// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatewright.Kubernetes;

/// <summary>Registers Kubernetes discovery.</summary>
public static class KubernetesServiceCollectionExtensions
{
    /// <summary>Registers the Kubernetes client, leader election and discovery when <c>Kubernetes:Enabled</c> is true. An <see cref="IDiscoveryStore"/> must be registered too.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><see langword="true"/> when discovery is enabled.</returns>
    public static bool AddKubernetesDiscoveryIfEnabled(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(KubernetesOptions.SectionName);
        if (!section.GetValue<bool>(nameof(KubernetesOptions.Enabled)))
        {
            return false;
        }

        services.Configure<KubernetesOptions>(section);
        services.TryAddSingleton<IKubernetes>(sp =>
        {
            var options = new KubernetesOptions();
            section.Bind(options);
            var config = string.IsNullOrWhiteSpace(options.KubeConfig)
                ? KubernetesClientConfiguration.InClusterConfig()
                : KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfig, options.KubeContext);
            return new k8s.Kubernetes(config);
        });
        services.AddSingleton<LeaseLeaderElection>();
        services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<LeaseLeaderElection>());
        services.AddHostedService(sp => sp.GetRequiredService<LeaseLeaderElection>());
        services.AddHostedService<KubernetesDiscoveryService>();
        services.TryAddSingleton<Gatewright.Core.Security.ISecretFetcher, KubernetesSecretFetcher>();
        return true;
    }
}
