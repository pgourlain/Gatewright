// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Acme.Gateway;

/// <summary>
/// The telemetry of the custom policy: its own span source and metric meter, plus the registration that makes the gateway export them.
/// Create these once (static), never per request.
/// </summary>
internal static class FraudTelemetry
{
    /// <summary>The name shared by the activity source and the meter. Gatewright only exports sources it is told about.</summary>
    public const string Name = "Acme.Gateway";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> Rejections = Meter.CreateCounter<long>("acme.fraud.rejections", description: "Orders rejected by the fraud check, by reason.");

    /// <summary>Tells the OpenTelemetry pipeline of the gateway to export this source and meter (OTLP when <c>Telemetry:Otlp:Endpoint</c> is set).</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddFraudTelemetry(this IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(Name));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(Name));
        return services;
    }
}
