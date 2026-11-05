// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatewright.State.Redis;

/// <summary>Registers the Redis-backed shared state.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the in-memory rate limit, quota and revocation stores with Redis implementations when <c>Redis:ConnectionString</c> is set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><see langword="true"/> when Redis is enabled.</returns>
    public static bool AddRedisStateIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(RedisOptions.SectionName);
        if (string.IsNullOrWhiteSpace(section[nameof(RedisOptions.ConnectionString)]))
        {
            return false;
        }

        services.Configure<RedisOptions>(section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RedisGuard>();
        services.Replace(ServiceDescriptor.Singleton<IRateLimitStore, RedisRateLimitStore>());
        services.Replace(ServiceDescriptor.Singleton<IQuotaStore, RedisQuotaStore>());
        services.Replace(ServiceDescriptor.Singleton<IRevocationFeed, RedisRevocationFeed>());
        return true;
    }
}
