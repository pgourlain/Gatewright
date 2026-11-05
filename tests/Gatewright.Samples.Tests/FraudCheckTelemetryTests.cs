// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Acme.Gateway;
using Gatewright.Core.Configuration;
using Gatewright.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Gatewright.Samples.Tests;

/// <summary>Shows what a custom distribution can emit: logs through ILogger, its own spans and metrics, and tags on the gateway request span.</summary>
// The policy telemetry (meter) is static, so tests that reject orders must not run in parallel.
[Collection("fraud-telemetry")]
public sealed class FraudCheckTelemetryTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Category, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                {
                    owner.Entries.Add((logLevel, category, formatter(state, exception)));
                }
            }
        }
    }

    private sealed record Captured(
        GatewayTestHost Host,
        CapturingLoggerProvider Logs,
        List<Activity> Activities,
        List<OpenTelemetry.Metrics.Metric> Metrics) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private static async Task<Captured> StartAsync()
    {
        var logs = new CapturingLoggerProvider();
        var activities = new List<Activity>();
        var metrics = new List<OpenTelemetry.Metrics.Metric>();
        var host = await GatewayTestHost.StartAsync(
            builder =>
            {
                builder.AddPolicy<FraudCheckPolicy>();
                builder.Builder.Logging.AddProvider(logs);
                builder.Services.AddFraudTelemetry();
                builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(activities));
                builder.Services.ConfigureOpenTelemetryMeterProvider(meters => meters.AddInMemoryExporter(metrics));
            },
            backend => new GatewayConfig
            {
                Apis = [GatewayTestHost.Api(backend, "orders", new PolicySet { Inbound = [GatewayTestHost.Policy("acme.fraudCheck", """{"maxAmount":1000}""")] })],
            });
        return new Captured(host, logs, activities, metrics);
    }

    private static async Task<HttpResponseMessage> PostAsync(GatewayTestHost host, string amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders/create") { Content = new StringContent("{}") };
        request.Headers.Add("x-order-amount", amount);
        return await host.Client.SendAsync(request);
    }

    [Fact]
    public async Task RejectedOrder_IsLoggedWithTheCustomLogger()
    {
        await using var captured = await StartAsync();

        await PostAsync(captured.Host, "5000");

        lock (captured.Logs.Entries)
        {
            var entry = captured.Logs.Entries.Where(e => e.Category == typeof(FraudCheckPolicy).FullName).ShouldHaveSingleItem();
            entry.Level.ShouldBe(LogLevel.Warning);
            entry.Message.ShouldBe("Order rejected (amount_too_high) for /orders/create");
        }
    }

    [Fact]
    public async Task RejectedOrder_ExportsTheCustomSpanAsAChildOfTheRequestSpan()
    {
        await using var captured = await StartAsync();

        await PostAsync(captured.Host, "5000");

        var fraud = captured.Activities.Where(a => a.Source.Name == FraudTelemetry.Name).ShouldHaveSingleItem();
        fraud.OperationName.ShouldBe("fraud-check");
        fraud.GetTagItem("acme.fraud.reason").ShouldBe("amount_too_high");
        var request = captured.Activities.Single(a => a.SpanId == fraud.ParentSpanId);
        request.GetTagItem("acme.fraud.rejected").ShouldBe(true);
    }

    [Fact]
    public async Task RejectedOrder_ExportsTheCustomCounter()
    {
        await using var captured = await StartAsync();
        await PostAsync(captured.Host, "5000");
        await PostAsync(captured.Host, "7000");
        await PostAsync(captured.Host, "10");

        captured.Host.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var metric = captured.Metrics.Where(m => m.Name == "acme.fraud.rejections").ShouldHaveSingleItem();
        long total = 0;
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            total += point.GetSumLong();
        }

        total.ShouldBe(2);
    }

    [Fact]
    public async Task AcceptedOrder_EmitsNoRejectionLog()
    {
        await using var captured = await StartAsync();

        await PostAsync(captured.Host, "10");

        lock (captured.Logs.Entries)
        {
            captured.Logs.Entries.ShouldNotContain(e => e.Category == typeof(FraudCheckPolicy).FullName);
        }
    }
}
