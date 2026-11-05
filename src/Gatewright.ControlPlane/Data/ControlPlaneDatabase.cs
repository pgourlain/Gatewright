// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Data;

/// <summary>Creates and seeds the control plane database.</summary>
public static class ControlPlaneDatabase
{
    /// <summary>The environments created on first start.</summary>
    public static readonly IReadOnlyList<string> DefaultEnvironments = ["dev", "staging", "prod"];

    /// <summary>Creates or upgrades the schema and seeds the default environments (and optionally a sample API).</summary>
    /// <param name="context">The database context.</param>
    /// <param name="seedSample">Whether to add the development sample (an <c>orders</c> API and product).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the database is ready.</returns>
    /// <remarks>On a relational provider with migrations (PostgreSQL), applies them (<c>Migrate</c>). On SQLite, which this project uses without migrations, creates the schema directly (<c>EnsureCreated</c>).</remarks>
    public static async Task InitializeAsync(GatewrightDbContext context, bool seedSample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var name in DefaultEnvironments)
        {
            if (!await context.Environments.AnyAsync(e => e.Name == name, cancellationToken).ConfigureAwait(false))
            {
                context.Environments.Add(new GatewayEnvironment { Name = name });
            }
        }

        if (!await context.ScopePolicies.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            context.ScopePolicies.Add(new ScopePoliciesEntity());
        }

        if (seedSample && !await context.Apis.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var product = new ProductEntity { Name = "starter", RequiresSubscription = false };
            var api = new ApiEntity
            {
                Name = "orders",
                DisplayName = "Orders API",
                BasePath = "/orders",
                Version = "v1",
                Backend = new Core.Configuration.BackendDefinition { Destinations = ["http://localhost:5101"] },
            };
            api.Products.Add(product);
            context.Apis.Add(api);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
