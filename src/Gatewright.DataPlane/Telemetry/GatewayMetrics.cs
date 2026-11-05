// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gatewright.DataPlane.Telemetry;

/// <summary>Business metrics of the gateway: traffic, latency and rejections per API, operation, product and (optionally) subscription.</summary>
public static class GatewayMetrics
{
    /// <summary>The meter name.</summary>
    public const string MeterName = "Gatewright.Gateway";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("gatewright.requests", description: "Requests handled by the gateway.");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("gatewright.request.duration", unit: "ms", description: "Time from receiving a request to completing its response.");

    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("gatewright.rejections", description: "Requests refused by a policy, by cause (error code).");

    /// <summary>Records one handled request.</summary>
    /// <param name="api">The API name.</param>
    /// <param name="operation">The operation name, or empty.</param>
    /// <param name="product">The product of the subscription, or empty.</param>
    /// <param name="subscription">The subscription id when the subscription dimension is on, otherwise <see langword="null"/>.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="rejection">The rejection code when a policy refused the request.</param>
    /// <param name="elapsedMs">The duration in milliseconds.</param>
    public static void Record(string api, string operation, string product, string? subscription, int statusCode, string? rejection, double elapsedMs)
    {
        var tags = new TagList
        {
            { "api", api },
            { "operation", operation },
            { "product", product },
            { "status_class", $"{statusCode / 100}xx" },
        };
        if (subscription is not null)
        {
            tags.Add("subscription", subscription);
        }

        Requests.Add(1, tags);
        Duration.Record(elapsedMs, tags);
        if (rejection is not null)
        {
            Rejections.Add(1, new TagList { { "api", api }, { "cause", rejection } });
        }
    }
}
