// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Distribution;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Distribution;

/// <summary>Registers snapshot distribution implementations.</summary>
public static class DistributionServiceCollectionExtensions
{
    /// <summary>Registers the in-process distributor used in mode <c>All</c>.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddInProcessDistribution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IConfigDistributor, InProcessConfigDistributor>();
        return services;
    }
}
