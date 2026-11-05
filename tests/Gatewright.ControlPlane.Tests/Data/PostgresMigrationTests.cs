// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Tests.Data;

[Collection(PostgresTestGroup.Name)]
public sealed class PostgresMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task InitializeAsync_AppliesMigrations_AndSeedsDefaultEnvironments()
    {
        await using var context = fixture.CreateContext();

        await ControlPlaneDatabase.InitializeAsync(context, seedSample: false, CancellationToken.None);

        (await context.Environments.Select(e => e.Name).OrderBy(n => n).ToListAsync()).ShouldBe(["dev", "prod", "staging"]);
        (await context.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_DoesNotDuplicateSeedData()
    {
        await using var first = fixture.CreateContext();
        await ControlPlaneDatabase.InitializeAsync(first, seedSample: false, CancellationToken.None);

        await using var second = fixture.CreateContext();
        await ControlPlaneDatabase.InitializeAsync(second, seedSample: false, CancellationToken.None);

        (await second.Environments.CountAsync(e => e.Name == "dev")).ShouldBe(1);
    }

    [Fact]
    public async Task Api_WithOperationsAndBackend_RoundTrips()
    {
        await using var write = fixture.CreateContext();
        await ControlPlaneDatabase.InitializeAsync(write, seedSample: false, CancellationToken.None);
        var api = new ApiEntity
        {
            Name = "orders-pg",
            BasePath = "/orders-pg",
            Backend = new Core.Configuration.BackendDefinition { Destinations = ["http://a"] },
        };
        api.Operations.Add(new OperationEntity { Name = "get", Method = "GET", Path = "/{id}" });
        write.Apis.Add(api);
        await write.SaveChangesAsync();

        await using var read = fixture.CreateContext();
        var loaded = await read.Apis.Include(a => a.Operations).SingleAsync(a => a.Name == "orders-pg");
        loaded.Backend.Destinations.ShouldBe(["http://a"]);
        loaded.Operations.ShouldHaveSingleItem().Path.ShouldBe("/{id}");
    }
}
