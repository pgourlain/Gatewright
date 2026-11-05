// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics;
using Gatewright.Abstractions;
using Gatewright.Sdk;

namespace Acme.Gateway;

/// <summary>Configuration of the <c>acme.fraudCheck</c> policy. The JSON Schema is generated from this type.</summary>
public sealed record FraudCheckConfig
{
    /// <summary>Gets the largest order amount accepted without review.</summary>
    [Description("Orders above this amount are rejected.")]
    public required decimal MaxAmount { get; init; }

    /// <summary>Gets the ISO country codes that are always rejected.</summary>
    [Description("Country codes (from the x-country header) that are blocked.")]
    public IReadOnlyList<string> BlockedCountries { get; init; } = [];
}

/// <summary>Rejects orders that look fraudulent, before they reach the backend.</summary>
[GatewayPolicy("acme.fraudCheck", Phases = PolicyPhase.Inbound)]
public sealed class FraudCheckPolicy : GatewayPolicy<FraudCheckConfig>
{
    // Cached delegates: no formatting or allocation unless the level is enabled.
    private static readonly Action<ILogger, string, string, Exception?> LogRejected = LoggerMessage.Define<string, string>(
        LogLevel.Warning, new EventId(1, "FraudRejected"), "Order rejected ({Reason}) for {Path}");

    protected override IPolicyInstance Create(FraudCheckConfig config, PolicyCompilationContext context)
    {
        // Compile once per snapshot: the per-request code below must stay cheap and allocation free.
        var blocked = new HashSet<string>(config.BlockedCountries, StringComparer.OrdinalIgnoreCase);
        var maxAmount = config.MaxAmount;
        var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger<FraudCheckPolicy>();

        return PolicyInstance.From(request =>
        {
            // A child span of the gateway request span (null, and free, when nothing listens to the source).
            using var span = FraudTelemetry.Source.StartActivity("fraud-check");
            var headers = request.HttpContext.Request.Headers;
            if (blocked.Contains(headers["x-country"].ToString()))
            {
                return Reject(logger, request, span, "blocked_country", "fraud_blocked_country", "Orders from this country are not accepted.");
            }

            if (decimal.TryParse(headers["x-order-amount"].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > maxAmount)
            {
                return Reject(logger, request, span, "amount_too_high", "fraud_amount_too_high", "This order needs a manual review.");
            }

            return PolicyResult.Continue;
        });
    }

    private static PolicyResult Reject(ILogger logger, GatewayContext request, Activity? span, string reason, string code, string message)
    {
        LogRejected(logger, reason, request.HttpContext.Request.Path.Value ?? "/", null);
        span?.SetTag("acme.fraud.reason", reason);
        // The ASP.NET Core request span is Activity.Current's parent: tag it too so the decision is visible on the whole request.
        span?.Parent?.SetTag("acme.fraud.rejected", true);
        FraudTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("reason", reason));
        return PolicyResult.Reject(403, code, message);
    }
}
