// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Exposure;

/// <summary>Declares a gateway policy on a controller or action (MVC). Use the dedicated attributes for common policies.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class GatewayPolicyAttribute : Attribute, IGatewayPolicyMetadataSource
{
    /// <summary>Initializes a new instance of the <see cref="GatewayPolicyAttribute"/> class.</summary>
    /// <param name="type">The policy type.</param>
    /// <param name="configurationJson">The policy configuration as JSON, or <see langword="null"/>.</param>
    public GatewayPolicyAttribute(string type, string? configurationJson = null)
    {
        Type = type;
        ConfigurationJson = configurationJson;
    }

    /// <summary>Gets the policy type.</summary>
    public string Type { get; }

    /// <summary>Gets the configuration as JSON.</summary>
    public string? ConfigurationJson { get; }

    /// <summary>Gets or sets the phase. Defaults to <c>inbound</c>.</summary>
    public string Phase { get; set; } = "inbound";

    /// <inheritdoc/>
    public GatewayPolicyMetadata ToMetadata() =>
        new(Type, ConfigurationJson is null ? null : System.Text.Json.JsonDocument.Parse(ConfigurationJson).RootElement.Clone(), Phase);
}

/// <summary>Limits calls on the gateway: <c>[GatewayRateLimit(100, 60)]</c> = 100 requests per 60 seconds, per subscription.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class GatewayRateLimitAttribute : Attribute, IGatewayPolicyMetadataSource
{
    /// <summary>Initializes a new instance of the <see cref="GatewayRateLimitAttribute"/> class.</summary>
    /// <param name="limit">The number of requests allowed per period.</param>
    /// <param name="periodSeconds">The period in seconds.</param>
    public GatewayRateLimitAttribute(int limit, int periodSeconds = 60)
    {
        Limit = limit;
        PeriodSeconds = periodSeconds;
    }

    /// <summary>Gets the number of requests allowed per period.</summary>
    public int Limit { get; }

    /// <summary>Gets the period in seconds.</summary>
    public int PeriodSeconds { get; }

    /// <summary>Gets or sets how requests are partitioned (<c>subscription</c>, <c>ip</c>, <c>global</c>, <c>claim:&lt;n&gt;</c>, <c>header:&lt;n&gt;</c>).</summary>
    public string PartitionBy { get; set; } = "subscription";

    /// <inheritdoc/>
    public GatewayPolicyMetadata ToMetadata() => new("rateLimit", new { limit = Limit, periodSeconds = PeriodSeconds, partitionBy = PartitionBy });
}

/// <summary>Caches successful responses on the gateway: <c>[CacheOnGateway(30)]</c> = 30 seconds.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheOnGatewayAttribute : Attribute, IGatewayPolicyMetadataSource
{
    /// <summary>Initializes a new instance of the <see cref="CacheOnGatewayAttribute"/> class.</summary>
    /// <param name="durationSeconds">How long responses are cached, in seconds.</param>
    public CacheOnGatewayAttribute(int durationSeconds) => DurationSeconds = durationSeconds;

    /// <summary>Gets the duration in seconds.</summary>
    public int DurationSeconds { get; }

    /// <inheritdoc/>
    public GatewayPolicyMetadata ToMetadata() => new("cache", new { durationSeconds = DurationSeconds });
}

/// <summary>Something that can describe itself as gateway policy metadata. Implemented by the gateway attributes.</summary>
public interface IGatewayPolicyMetadataSource
{
    /// <summary>Builds the metadata.</summary>
    /// <returns>The policy metadata.</returns>
    GatewayPolicyMetadata ToMetadata();
}
