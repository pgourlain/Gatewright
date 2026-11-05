// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>Builds the <see cref="GatewayConfig"/> described by the current draft state of the database. Output is deterministic (sorted by name).</summary>
public sealed class ConfigExporter
{
    private readonly GatewrightDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="ConfigExporter"/> class.</summary>
    /// <param name="db">The database context.</param>
    public ConfigExporter(GatewrightDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>Exports the draft as a configuration with <see cref="GatewayConfig.Version"/> 0.</summary>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The configuration.</returns>
    public async Task<GatewayConfig> ExportAsync(CancellationToken cancellationToken)
    {
        var scope = await _db.ScopePolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var hostnames = await _db.Hostnames.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var shared = await _db.SharedPolicies.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var products = await _db.Products.AsNoTracking().Include(p => p.Plans).ToListAsync(cancellationToken).ConfigureAwait(false);
        var apis = await _db.Apis.AsNoTracking().Include(a => a.Operations).Include(a => a.Products).AsSplitQuery()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var subscriptions = await _db.Subscriptions.AsNoTracking().Include(s => s.Keys).ToListAsync(cancellationToken).ConfigureAwait(false);

        var productNames = products.ToDictionary(p => p.Id, p => p.Name);
        var planNames = products.SelectMany(p => p.Plans).ToDictionary(p => p.Id, p => p.Name);

        return new GatewayConfig
        {
            Version = 0,
            GlobalPolicies = scope?.Global ?? PolicySet.Empty,
            GatewayPolicies = scope?.Gateway ?? PolicySet.Empty,
            Hostnames =
            [
                .. hostnames.OrderBy(h => h.Name, StringComparer.Ordinal).Select(h => new HostnameDefinition
                {
                    Name = h.Name,
                    Certificate = h.Certificate,
                    RedirectHttpToHttps = h.RedirectHttpToHttps,
                    Hsts = h.Hsts,
                }),
            ],
            Policies = shared.OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Definition),
            Products =
            [
                .. products.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new ProductDefinition
                {
                    Name = p.Name,
                    RequiresSubscription = p.RequiresSubscription,
                    Policies = p.Policies,
                }),
            ],
            Subscriptions =
            [
                .. subscriptions.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => new SubscriptionDefinition
                {
                    Id = s.Name,
                    Product = productNames.GetValueOrDefault(s.ProductId, ""),
                    Plan = s.PlanId is { } planId ? planNames.GetValueOrDefault(planId) : null,
                    Active = s.Active,
                    KeyHashes = [.. s.ActiveKeys.Select(k => k.Hash).Order(StringComparer.Ordinal)],
                }),
            ],
            Apis =
            [
                .. apis.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => new ApiDefinition
                {
                    Name = a.Name,
                    BasePath = a.BasePath,
                    Version = a.Version,
                    Backend = a.Backend,
                    Policies = a.Policies,
                    Products = [.. a.Products.Select(p => p.Name).Order(StringComparer.Ordinal)],
                    Operations =
                    [
                        .. a.Operations.OrderBy(o => o.Name, StringComparer.Ordinal).Select(o => new OperationDefinition
                        {
                            Name = o.Name,
                            Method = o.Method,
                            Path = o.Path,
                            Policies = o.Policies,
                        }),
                    ],
                }),
            ],
        };
    }
}
