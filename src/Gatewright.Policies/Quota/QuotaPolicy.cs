// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Globalization;
using Gatewright.Abstractions;
using Gatewright.Core.Security;
using Gatewright.Policies.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Policies.Quota;

/// <summary>The <c>quota</c> policy: a calendar-period request budget per subscription (per client IP when there is no subscription).</summary>
public sealed class QuotaPolicy : PolicyBase<QuotaConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["limit"],
          "properties": {
            "limit": { "type": "integer", "minimum": 1 },
            "period": { "enum": ["hour", "day", "month"] },
            "name": { "type": "string", "minLength": 1 }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="QuotaPolicy"/> class.</summary>
    public QuotaPolicy()
        : base("quota", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(QuotaConfig config, PolicyCompilationContext context) => new Instance(
        config.Name ?? context.Scope,
        config.Limit,
        config.Period,
        context.Services.GetRequiredService<IQuotaStore>(),
        context.Services.GetService<TimeProvider>() ?? TimeProvider.System);

    private sealed class Instance(string name, long limit, string period, IQuotaStore store, TimeProvider time) : IPolicyInstance
    {
        public async ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var now = time.GetUtcNow();
            var (start, end) = Window(now);
            var partition = context.Subscription is { } s
                ? "sub:" + s.Id
                : "ip:" + (context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            var key = string.Create(CultureInfo.InvariantCulture, $"{name}|{partition}|{start.UtcTicks}");

            var result = await store.TryConsumeAsync(key, limit, end, context.CancellationToken).ConfigureAwait(false);

            var headers = context.HttpContext.Response.Headers;
            headers["X-Quota-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
            headers["X-Quota-Remaining"] = result.Remaining.ToString(CultureInfo.InvariantCulture);
            headers["X-Quota-Reset"] = end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

            if (result.Allowed)
            {
                return PolicyResult.Continue;
            }

            headers["Retry-After"] = Math.Max(1, (long)Math.Ceiling((end - now).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return PolicyResult.Reject(429, "quota_exceeded", "Quota exceeded for the current period.");
        }

        private (DateTimeOffset Start, DateTimeOffset End) Window(DateTimeOffset now)
        {
            switch (period)
            {
                case "hour":
                    var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
                    return (hour, hour.AddHours(1));
                case "day":
                    var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
                    return (day, day.AddDays(1));
                default:
                    var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                    return (month, month.AddMonths(1));
            }
        }
    }
}
