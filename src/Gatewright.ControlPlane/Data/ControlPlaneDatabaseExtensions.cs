// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.ControlPlane;

/// <summary>Registers the control plane database.</summary>
public static class ControlPlaneDatabaseExtensions
{
    /// <summary>Registers <see cref="GatewrightDbContext"/> on SQLite (no external dependency; schema created with <c>EnsureCreated</c>, no migrations).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddControlPlaneSqlite(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<GatewrightDbContext>(o => o.UseSqlite(connectionString));
        return services;
    }

    /// <summary>Registers <see cref="GatewrightDbContext"/> on PostgreSQL; schema created and upgraded with versioned migrations.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddControlPlanePostgres(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<GatewrightDbContext>(o => o.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(GatewrightDbContext).Assembly.FullName)));
        return services;
    }
}
