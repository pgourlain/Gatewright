// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Gatewright.Core.Configuration;
using Gatewright.DataPlane.Telemetry;

namespace Gatewright.IntegrationTests;

public sealed class MetricsTests
{
    private sealed record Measurement(string Instrument, long Value, Dictionary<string, object?> Tags);

    private static async Task<List<Measurement>> RunAsync(Func<TestGateway, Task> act, Func<string, GatewayConfig> config)
    {
        var measurements = new ConcurrentBag<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == GatewayMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => measurements.Add(new Measurement(i.Name, v, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((i, _, tags, _) => measurements.Add(new Measurement(i.Name, 1, ToDictionary(tags))));
        listener.Start();
        await using var gateway = await TestGateway.StartAsync(config);
        await act(gateway);
        return [.. measurements];
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            result[tag.Key] = tag.Value;
        }

        return result;
    }

    [Fact]
    public async Task Request_Succeeds_RecordsCountAndDurationWithApiTags()
    {
        var measurements = await RunAsync(
            async g => (await g.Client.GetAsync("/metrics-ok/x")).EnsureSuccessStatusCode(),
            url => new GatewayConfig { Apis = [TestGateway.Api(url, name: "metrics-ok")] });

        var requests = measurements.Single(m => m.Instrument == "gatewright.requests" && (string?)m.Tags["api"] == "metrics-ok");
        requests.Tags["status_class"].ShouldBe("2xx");
        measurements.ShouldContain(m => m.Instrument == "gatewright.request.duration" && (string?)m.Tags["api"] == "metrics-ok");
        measurements.ShouldNotContain(m => m.Instrument == "gatewright.rejections" && (string?)m.Tags["api"] == "metrics-ok");
    }

    [Fact]
    public async Task Request_RejectedByPolicy_RecordsRejectionCause()
    {
        var measurements = await RunAsync(
            async g => (await g.Client.GetAsync("/metrics-rej/x")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized),
            url => new GatewayConfig
            {
                Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("apiKey")] }, name: "metrics-rej")],
            });

        var rejection = measurements.Single(m => m.Instrument == "gatewright.rejections" && (string?)m.Tags["api"] == "metrics-rej");
        rejection.Tags["cause"].ShouldNotBeNull();
        measurements.Single(m => m.Instrument == "gatewright.requests" && (string?)m.Tags["api"] == "metrics-rej").Tags["status_class"].ShouldBe("4xx");
    }
}
