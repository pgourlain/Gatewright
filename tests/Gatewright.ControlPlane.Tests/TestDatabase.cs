// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Tests;

/// <summary>An isolated in-memory SQLite database that lives as long as this object.</summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<GatewrightDbContext>().UseSqlite(_connection).Options;
        using var context = CreateContext();
        ControlPlaneDatabase.InitializeAsync(context, seedSample: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    public DbContextOptions<GatewrightDbContext> Options { get; }

    public GatewrightDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
