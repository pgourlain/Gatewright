// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Gatewright.ControlPlane.Tests.Data;

/// <summary>A real PostgreSQL (Testcontainers) shared by the tests of this assembly. Requires Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public GatewrightDbContext CreateContext() => new(
        new DbContextOptionsBuilder<GatewrightDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
