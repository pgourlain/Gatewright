// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Globalization;
using Gatewright.Abstractions;
using Gatewright.Core.Security;
using Gatewright.Policies.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatewright.Policies.RateLimit;

/// <summary>
/// The <c>rateLimit</c> policy: token bucket or fixed window per partition (subscription, IP, claim, header), with
/// <c>RateLimit-*</c> and <c>Retry-After</c> headers. Counters are local to the process; shared counters arrive with the Redis store.
/// </summary>
public sealed class RateLimitPolicy : PolicyBase<RateLimitConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["limit"],
          "properties": {
            "algorithm": { "enum": ["tokenBucket", "fixedWindow"] },
            "limit": { "type": "integer", "minimum": 1 },
            "periodSeconds": { "type": "integer", "minimum": 1 },
            "burst": { "type": "integer", "minimum": 1 },
            "partitionBy": { "type": "string", "pattern": "^(subscription|ip|global|claim:.+|header:.+)$" },
            "shared": { "type": "boolean" },
            "batchSize": { "type": "integer", "minimum": 1 },
            "onStoreFailure": { "enum": ["failOpen", "failClosed"] },
            "name": { "type": "string", "minLength": 1 }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="RateLimitPolicy"/> class.</summary>
    public RateLimitPolicy()
        : base("rateLimit", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(RateLimitConfig config, PolicyCompilationContext context)
    {
        var registry = context.Services.GetService<RateLimiterRegistry>() ?? new RateLimiterRegistry();
        var time = context.Services.GetService<TimeProvider>() ?? TimeProvider.System;
        var name = config.Name ?? context.Scope;
        var partitioner = Partitioner.Parse(config.PartitionBy);
        if (config.Shared)
        {
            if (config.Algorithm != "tokenBucket")
            {
                throw new ArgumentException("Shared rate limits use the token bucket algorithm.");
            }

            var shared = registry.GetOrCreateShared(
                name,
                config.Limit,
                config.PeriodSeconds,
                config.Burst ?? config.Limit,
                config.BatchSize,
                config.OnStoreFailure == "failOpen",
                context.Services.GetService<IRateLimitStore>() ?? new InMemoryRateLimitStore(time),
                time,
                context.Services.GetService<ILoggerFactory>()?.CreateLogger("Gatewright.Policies.RateLimit"));
            return new SharedInstance(shared, partitioner);
        }

        var limiter = registry.GetOrCreate(
            name,
            config.Algorithm == "tokenBucket",
            config.Limit,
            config.PeriodSeconds,
            config.Burst ?? config.Limit,
            time);
        return new Instance(limiter, partitioner);
    }

    private abstract class Partitioner
    {
        public abstract string Resolve(GatewayContext context);

        public static Partitioner Parse(string spec) => spec switch
        {
            "global" => new Constant("global"),
            "ip" => new Ip(),
            "subscription" => new Subscription(),
            _ when spec.StartsWith("claim:", StringComparison.Ordinal) => new Claim(spec[6..]),
            _ when spec.StartsWith("header:", StringComparison.Ordinal) => new Header(spec[7..]),
            _ => throw new ArgumentException($"Unknown partition '{spec}'."),
        };

        private sealed class Constant(string value) : Partitioner
        {
            public override string Resolve(GatewayContext context) => value;
        }

        private sealed class Ip : Partitioner
        {
            public override string Resolve(GatewayContext context) =>
                "ip:" + (context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        private sealed class Subscription : Partitioner
        {
            public override string Resolve(GatewayContext context) => context.Subscription is { } s
                ? "sub:" + s.Id
                : "ip:" + (context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        private sealed class Claim(string name) : Partitioner
        {
            public override string Resolve(GatewayContext context) =>
                "claim:" + (context.Identity?.FindFirst(name)?.Value ?? "anonymous");
        }

        private sealed class Header(string name) : Partitioner
        {
            public override string Resolve(GatewayContext context) =>
                "header:" + context.HttpContext.Request.Headers[name].ToString();
        }
    }

    private sealed class Instance(LocalRateLimiter limiter, Partitioner partitioner) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var decision = limiter.Acquire(partitioner.Resolve(context));
            var headers = context.HttpContext.Response.Headers;
            headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Reset"] = decision.ResetSeconds.ToString(CultureInfo.InvariantCulture);

            if (decision.Allowed)
            {
                return new ValueTask<PolicyResult>(PolicyResult.Continue);
            }

            headers["Retry-After"] = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return new ValueTask<PolicyResult>(PolicyResult.Reject(429, "rate_limited", "Rate limit exceeded."));
        }
    }

    private sealed class SharedInstance(SharedRateLimiter limiter, Partitioner partitioner) : IPolicyInstance
    {
        public async ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var decision = await limiter.AcquireAsync(partitioner.Resolve(context), context.CancellationToken).ConfigureAwait(false);
            if (decision.StoreUnavailable)
            {
                return decision.Allowed
                    ? PolicyResult.Continue
                    : PolicyResult.Reject(503, "rate_limit_unavailable", "The rate limit service is unavailable.");
            }

            var headers = context.HttpContext.Response.Headers;
            headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
            if (decision.Allowed)
            {
                return PolicyResult.Continue;
            }

            headers["Retry-After"] = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return PolicyResult.Reject(429, "rate_limited", "Rate limit exceeded.");
        }
    }
}
