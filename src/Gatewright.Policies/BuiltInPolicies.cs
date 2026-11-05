// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Security;
using Gatewright.Policies.ApiKey;
using Gatewright.Policies.Cache;
using Gatewright.Policies.Cors;
using Gatewright.Policies.Headers;
using Gatewright.Policies.IpFilter;
using Gatewright.Policies.Jwt;
using Gatewright.Policies.Mock;
using Gatewright.Policies.Quota;
using Gatewright.Policies.RateLimit;
using Gatewright.Policies.Resilience;
using Gatewright.Policies.Rewrite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatewright.Policies;

/// <summary>The policies shipped with Gatewright.</summary>
public static class BuiltInPolicies
{
    /// <summary>Creates one instance of each built-in policy type.</summary>
    /// <returns>The policies.</returns>
    public static IReadOnlyList<IGatewayPolicy> Create() =>
    [
        new HeadersPolicy(),
        new MockResponsePolicy(),
        new RewritePolicy(),
        new TimeoutPolicy(),
        new RetryPolicy(),
        new CircuitBreakerPolicy(),
        new CachePolicy(),
        new QuotaPolicy(),
        new RateLimitPolicy(),
        new ApiKeyPolicy(),
        new CorsPolicy(),
        new IpFilterPolicy(),
        new JwtPolicy(),
    ];

    /// <summary>Registers every built-in policy in dependency injection.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBuiltInPolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new ApiKeyHasher(null));
        services.TryAddSingleton<InMemorySubscriptionStore>();
        services.TryAddSingleton<ISubscriptionStore>(sp => sp.GetRequiredService<InMemorySubscriptionStore>());
        services.AddSingleton<ISecurityDataSink>(sp => sp.GetRequiredService<InMemorySubscriptionStore>());
        services.AddSingleton<IRevocationSink>(sp => sp.GetRequiredService<InMemorySubscriptionStore>());
        services.AddOutputCache();
        services.TryAddSingleton<CacheInvalidator>();
        services.TryAddSingleton<RateLimiterRegistry>();
        services.TryAddSingleton<CircuitBreakerRegistry>();
        services.TryAddSingleton<IQuotaStore, InMemoryQuotaStore>();
        services.TryAddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        services.TryAddSingleton<IJwksFetcher, HttpJwksFetcher>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JwksStore>();
        services.AddHostedService(sp => new JwksRefreshService(sp.GetRequiredService<JwksStore>()));
        foreach (var policy in Create())
        {
            services.AddSingleton(policy);
        }

        return services;
    }
}
