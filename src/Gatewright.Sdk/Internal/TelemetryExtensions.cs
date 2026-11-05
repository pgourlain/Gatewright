// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.DataPlane.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Gatewright.Sdk.Internal;

/// <summary>Wires OpenTelemetry traces, metrics and logs. Export is OTLP, enabled by <c>Telemetry:Otlp:Endpoint</c> (or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>).</summary>
internal static class TelemetryExtensions
{
    public static void AddGatewrightTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection(TelemetryOptions.SectionName));
        var endpoint = builder.Configuration["Telemetry:Otlp:Endpoint"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var export = !string.IsNullOrWhiteSpace(endpoint);
        var http = string.Equals(builder.Configuration["Telemetry:Otlp:Protocol"] ?? builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"], "http/protobuf", StringComparison.OrdinalIgnoreCase);
        var serviceName = builder.Configuration["Telemetry:ServiceName"] ?? "gatewright";

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("Gatewright.*", "Yarp.ReverseProxy");
                if (export)
                {
                    t.AddOtlpExporter(o => Configure(o, endpoint!, http, "traces"));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter("Gatewright.*", "Yarp.ReverseProxy", "Microsoft.AspNetCore.Server.Kestrel");
                if (export)
                {
                    m.AddOtlpExporter(o => Configure(o, endpoint!, http, "metrics"));
                }
            });
        if (export)
        {
            otel.WithLogging(l => l.AddOtlpExporter(o => Configure(o, endpoint!, http, "logs")));
        }
    }

    // With OTLP/HTTP the exporter needs the full signal URL (for example http://host:4318/v1/traces); gRPC uses the base address.
    private static void Configure(OtlpExporterOptions options, string endpoint, bool http, string signal)
    {
        if (http)
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri(endpoint.TrimEnd('/') + "/v1/" + signal);
        }
        else
        {
            options.Endpoint = new Uri(endpoint);
        }
    }
}
