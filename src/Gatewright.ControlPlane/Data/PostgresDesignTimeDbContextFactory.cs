// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gatewright.ControlPlane.Data;

/// <summary>Builds a <see cref="GatewrightDbContext"/> on PostgreSQL for <c>dotnet ef migrations</c>. Not used at runtime.</summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GatewrightDbContext>
{
    /// <inheritdoc/>
    public GatewrightDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<GatewrightDbContext>();
        builder.UseNpgsql("Host=localhost;Database=gatewright;Username=gatewright;Password=gatewright");
        return new GatewrightDbContext(builder.Options);
    }
}
