// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Exposure;

/// <summary>Minimal API extensions that attach gateway policies to endpoints. They only describe the policies: the gateway enforces them.</summary>
public static class GatewayEndpointExtensions
{
    /// <summary>Attaches any gateway policy to the endpoint.</summary>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="type">The policy type, for example <c>jwt</c> or <c>acme.fraudCheck</c>.</param>
    /// <param name="configuration">The policy configuration (an anonymous object, a record or a <see cref="System.Text.Json.JsonElement"/>).</param>
    /// <param name="phase">The phase (<c>inbound</c> by default).</param>
    /// <returns>The builder.</returns>
    public static TBuilder WithGatewayPolicy<TBuilder>(this TBuilder builder, string type, object? configuration = null, string phase = "inbound")
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var metadata = new GatewayPolicyMetadata(type, configuration, phase);
        builder.Add(endpoint => endpoint.Metadata.Add(metadata));
        return builder;
    }

    /// <summary>Limits calls: <paramref name="limit"/> requests per <paramref name="period"/>.</summary>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="limit">The number of requests allowed per period.</param>
    /// <param name="period">The period.</param>
    /// <param name="partitionBy">How requests are partitioned (<c>subscription</c> by default).</param>
    /// <returns>The builder.</returns>
    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, int limit, TimeSpan period, string partitionBy = "subscription")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithGatewayPolicy("rateLimit", new { limit, periodSeconds = (int)period.TotalSeconds, partitionBy });

    /// <summary>Caches successful responses on the gateway for <paramref name="duration"/>.</summary>
    /// <typeparam name="TBuilder">The endpoint builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="duration">How long responses are cached.</param>
    /// <returns>The builder.</returns>
    public static TBuilder CacheOnGateway<TBuilder>(this TBuilder builder, TimeSpan duration)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithGatewayPolicy("cache", new { durationSeconds = (int)duration.TotalSeconds });
}
