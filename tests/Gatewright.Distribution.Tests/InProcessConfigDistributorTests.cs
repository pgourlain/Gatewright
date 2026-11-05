// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Snapshots;

namespace Gatewright.Distribution.Tests;

public sealed class InProcessConfigDistributorTests
{
    private static ConfigSnapshot Snapshot(long version) =>
        new(version, "h" + version, [], [], new Dictionary<string, RoutePipeline>(), SecurityData.Empty);

    [Fact]
    public async Task Subscribe_AfterPublish_ReceivesLatestSnapshot()
    {
        var distributor = new InProcessConfigDistributor();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await distributor.PublishAsync(Snapshot(1), cts.Token);

        await using var enumerator = distributor.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        enumerator.Current.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Publish_SeveralBeforeConsumption_KeepsOnlyLatest()
    {
        var distributor = new InProcessConfigDistributor();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await distributor.PublishAsync(Snapshot(1), cts.Token);
        await distributor.PublishAsync(Snapshot(2), cts.Token);
        await distributor.PublishAsync(Snapshot(3), cts.Token);

        await using var enumerator = distributor.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        enumerator.Current.Version.ShouldBe(3);
    }

    [Fact]
    public async Task Subscribe_WaitingConsumer_ReceivesPublishedSnapshot()
    {
        var distributor = new InProcessConfigDistributor();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = distributor.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var next = enumerator.MoveNextAsync().AsTask();

        await distributor.PublishAsync(Snapshot(9), cts.Token);

        (await next).ShouldBeTrue();
        enumerator.Current.Version.ShouldBe(9);
    }
}
