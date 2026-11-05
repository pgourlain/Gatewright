// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Core;

/// <summary>Registers the core services shared by control plane and data plane.</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the policy registry, validator, compiler and snapshot translator. Policies are taken from every registered <see cref="IGatewayPolicy"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGatewrightCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new PolicyPipelineOptions());
        services.AddSingleton(sp => new PolicyRegistry(sp.GetServices<IGatewayPolicy>()));
        services.AddSingleton(sp => new GatewayConfigValidator(sp.GetRequiredService<PolicyRegistry>()));
        services.AddSingleton(sp => new PolicyCompiler(sp.GetRequiredService<PolicyRegistry>(), sp));
        services.AddSingleton(sp => new SnapshotTranslator(
            sp.GetRequiredService<GatewayConfigValidator>(),
            sp.GetRequiredService<PolicyCompiler>(),
            sp.GetRequiredService<PolicyPipelineOptions>()));
        return services;
    }
}
