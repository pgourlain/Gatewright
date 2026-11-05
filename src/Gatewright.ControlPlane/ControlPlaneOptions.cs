// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane;

/// <summary>Control plane settings, bound from <c>ControlPlane</c>.</summary>
public sealed class ControlPlaneOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ControlPlane";

    /// <summary>Gets or sets the database provider. Defaults to <see cref="DatabaseProvider.Sqlite"/> (no external dependency, used by dev and by the samples without a container).</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Gets or sets the connection string of the control plane database, for the configured <see cref="Provider"/>.</summary>
    public string ConnectionString { get; set; } = "Data Source=gatewright-controlplane.db";

    /// <summary>Gets or sets a value indicating whether the development sample (an <c>orders</c> API and a <c>starter</c> product) is created on an empty database.</summary>
    public bool SeedSample { get; set; }
}

/// <summary>The control plane database provider.</summary>
public enum DatabaseProvider
{
    /// <summary>SQLite, file based, schema created with <c>EnsureCreated</c> (no migrations). No external dependency.</summary>
    Sqlite,

    /// <summary>PostgreSQL, schema created and upgraded with versioned EF Core migrations.</summary>
    Postgres,
}
