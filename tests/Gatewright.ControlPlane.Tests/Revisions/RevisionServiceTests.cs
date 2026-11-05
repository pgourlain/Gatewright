// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Tests.Revisions;

public sealed class RevisionServiceTests
{
    [Fact]
    public async Task CaptureDraft_Changes_CreateNumberedRevisions()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        var (first, createdFirst) = await h.Service.CaptureDraftAsync("alice", "add orders", CancellationToken.None);
        h.AddApi("invoices");
        var (second, createdSecond) = await h.Service.CaptureDraftAsync("bob", "add invoices", CancellationToken.None);

        createdFirst.ShouldBeTrue();
        createdSecond.ShouldBeTrue();
        first.Number.ShouldBe(1);
        second.Number.ShouldBe(2);
        second.CreatedBy.ShouldBe("bob");
        (await h.Service.ListAsync(10, CancellationToken.None)).Select(r => r.Number).ShouldBe([2, 1]);
    }

    [Fact]
    public async Task CaptureDraft_NothingChanged_ReturnsExistingRevision()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        var (first, _) = await h.Service.CaptureDraftAsync("alice", "x", CancellationToken.None);

        var (again, created) = await h.Service.CaptureDraftAsync("alice", "x", CancellationToken.None);

        created.ShouldBeFalse();
        again.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Publish_ValidRevision_BecomesCurrentWithVersionedSnapshot()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        await h.Service.CaptureDraftAsync("alice", "x", CancellationToken.None);

        var result = await h.Service.PublishAsync(1, "dev", "alice", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Publication!.Number.ShouldBe(1);
        var current = await h.Service.GetCurrentAsync("dev", CancellationToken.None);
        current!.Id.ShouldBe(result.Publication.Id);
        var snapshot = GatewayConfigSerializer.Deserialize(current.SnapshotJson);
        snapshot.Version.ShouldBe(1);
        snapshot.Apis.ShouldHaveSingleItem().Name.ShouldBe("orders");
        current.SnapshotHash.Length.ShouldBe(64);
        (await h.Service.GetCurrentAsync("prod", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Publish_InvalidConfig_IsRefusedWithErrorsAndNothingChanges()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("a", "/same");
        h.AddApi("b", "/same");
        await h.Service.CaptureDraftAsync("alice", "conflict", CancellationToken.None);

        var result = await h.Service.PublishAsync(1, "dev", "alice", CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.ShouldBe(PublishFailure.Invalid);
        result.Errors.ShouldContain(e => e.Contains("api.path-conflict"));
        (await h.Service.GetCurrentAsync("dev", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Publish_UnknownRevisionOrEnvironment_IsRefused()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        await h.Service.CaptureDraftAsync("alice", "x", CancellationToken.None);

        (await h.Service.PublishAsync(99, "dev", "alice", CancellationToken.None)).Failure.ShouldBe(PublishFailure.RevisionNotFound);
        (await h.Service.PublishAsync(1, "nowhere", "alice", CancellationToken.None)).Failure.ShouldBe(PublishFailure.EnvironmentNotFound);
    }

    [Fact]
    public async Task Lifecycle_DraftPublishedRollback_RestoresPreviousConfiguration()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        await h.Service.CaptureDraftAsync("alice", "v1", CancellationToken.None);
        await h.Service.PublishAsync(1, "prod", "alice", CancellationToken.None);
        h.AddApi("invoices");
        await h.Service.CaptureDraftAsync("alice", "v2", CancellationToken.None);
        await h.Service.PublishAsync(2, "prod", "alice", CancellationToken.None);
        (await Current(h)).Apis.Count.ShouldBe(2);

        var rollback = await h.Service.RollbackAsync(1, "prod", "bob", CancellationToken.None);

        rollback.IsSuccess.ShouldBeTrue();
        rollback.Publication!.Kind.ShouldBe(PublicationKind.Rollback);
        rollback.Publication.Number.ShouldBe(3);
        var restored = await Current(h);
        restored.Apis.ShouldHaveSingleItem().Name.ShouldBe("orders");
        restored.Version.ShouldBe(3);
        (await h.Service.ListPublicationsAsync("prod", CancellationToken.None)).Select(p => p.Number).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task Rollback_RevisionNeverPublishedToEnvironment_IsRefused()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        await h.Service.CaptureDraftAsync("alice", "v1", CancellationToken.None);
        await h.Service.PublishAsync(1, "dev", "alice", CancellationToken.None);

        (await h.Service.RollbackAsync(1, "prod", "alice", CancellationToken.None)).Failure.ShouldBe(PublishFailure.NeverPublished);
    }

    [Fact]
    public async Task Diff_BetweenRevisions_ListsAddedRemovedAndChanged()
    {
        using var h = new RevisionTestHarness();
        var orders = h.AddApi("orders");
        h.AddApi("legacy");
        await h.Service.CaptureDraftAsync("alice", "v1", CancellationToken.None);
        orders.Backend = new BackendDefinition { Destinations = ["http://new-backend"] };
        h.Db.Apis.Remove(h.Db.Apis.Single(a => a.Name == "legacy"));
        h.AddApi("invoices");
        await h.Service.CaptureDraftAsync("alice", "v2", CancellationToken.None);

        var diff = await h.Service.DiffAsync(1, 2, CancellationToken.None);

        diff.ShouldNotBeNull();
        diff.ShouldContain(d => d.Kind == DiffKind.Added && d.Path == "apis.invoices");
        diff.ShouldContain(d => d.Kind == DiffKind.Removed && d.Path == "apis.legacy");
        diff.ShouldContain(d => d.Kind == DiffKind.Changed && d.Path == "apis.orders.backend.destinations[0]" && d.After == "\"http://new-backend\"");
        (await h.Service.DiffAsync(1, 99, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Diff_IdenticalRevisions_IsEmpty()
    {
        using var h = new RevisionTestHarness();
        h.AddApi("orders");
        await h.Service.CaptureDraftAsync("alice", "v1", CancellationToken.None);

        (await h.Service.DiffAsync(1, 1, CancellationToken.None))!.ShouldBeEmpty();
    }

    private static async Task<GatewayConfig> Current(RevisionTestHarness h) =>
        GatewayConfigSerializer.Deserialize((await h.Service.GetCurrentAsync("prod", CancellationToken.None))!.SnapshotJson);
}
