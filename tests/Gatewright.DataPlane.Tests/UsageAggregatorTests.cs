// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.DataPlane.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.DataPlane.Tests;

public sealed class UsageAggregatorTests
{
    private sealed class Sink(bool fail = false) : IUsageSink
    {
        public List<UsageRecord> Records { get; } = [];

        public string Name => "test";

        public Task WriteAsync(IReadOnlyList<UsageRecord> records, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidOperationException("boom");
            }

            Records.AddRange(records);
            return Task.CompletedTask;
        }
    }

    private static UsageAggregator Create(FakeTimeProvider time, params IUsageSink[] sinks) => new(
        sinks,
        Options.Create(new TelemetryOptions { Usage = new UsageOptions { Enabled = true } }),
        time,
        NullLogger<UsageAggregator>.Instance);

    [Fact]
    public async Task Flush_AggregatesPerKey_AndResets()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var sink = new Sink();
        var aggregator = Create(time, sink);
        aggregator.Record("a", "op", "p", "s1", 200, 10);
        aggregator.Record("a", "op", "p", "s1", 500, 30);
        aggregator.Record("a", "op", "p", "s2", 200, 5);
        time.Advance(TimeSpan.FromMinutes(1));

        await aggregator.FlushAsync(CancellationToken.None);

        sink.Records.Count.ShouldBe(2);
        var s1 = sink.Records.Single(r => r.Subscription == "s1");
        s1.Requests.ShouldBe(2);
        s1.Errors.ShouldBe(1);
        s1.TotalDurationMs.ShouldBe(40);
        s1.WindowEnd.ShouldBe(s1.WindowStart.AddMinutes(1));

        await aggregator.FlushAsync(CancellationToken.None);
        sink.Records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Flush_FailingSink_DoesNotStopOtherSinks()
    {
        var good = new Sink();
        var aggregator = Create(new FakeTimeProvider(), new Sink(fail: true), good);
        aggregator.Record("a", "", "", "", 200, 1);

        await aggregator.FlushAsync(CancellationToken.None);

        good.Records.Count.ShouldBe(1);
    }

    [Fact]
    public void Record_Disabled_DoesNothing()
    {
        var aggregator = new UsageAggregator([new Sink()], Options.Create(new TelemetryOptions()), TimeProvider.System, NullLogger<UsageAggregator>.Instance);
        aggregator.Enabled.ShouldBeFalse();
    }
}
