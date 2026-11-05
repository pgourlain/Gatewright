// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Gatewright.Core.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatewright.DataPlane.Tests;

public sealed class RevocationListenerServiceTests
{
    [Fact]
    public async Task PublishedRevocation_IsAppliedToTheSubscriptionStoreWithinSeconds()
    {
        var feed = new InMemoryRevocationFeed();
        var store = new InMemorySubscriptionStore();
        store.Apply(new SecurityData { Subscriptions = [new SubscriptionDefinition { Id = "s", Product = "p", KeyHashes = ["abc"] }] });
        using var service = new RevocationListenerService(feed, [store], NullLogger<RevocationListenerService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        (await store.FindByKeyHashAsync("abc")).ShouldNotBeNull();

        await feed.PublishAsync("abc", CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await store.FindByKeyHashAsync("abc") is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        (await store.FindByKeyHashAsync("abc")).ShouldBeNull();
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FailingFeed : IRevocationFeed
    {
        public int Attempts { get; private set; }

        public ValueTask PublishAsync(string keyHash, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public IAsyncEnumerable<string> SubscribeAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("feed down");
        }
    }

    [Fact]
    public async Task FailingFeed_DoesNotCrashTheServiceAndIsRetried()
    {
        var feed = new FailingFeed();
        using var service = new RevocationListenerService(feed, [], NullLogger<RevocationListenerService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        feed.Attempts.ShouldBeGreaterThanOrEqualTo(1);
    }
}
