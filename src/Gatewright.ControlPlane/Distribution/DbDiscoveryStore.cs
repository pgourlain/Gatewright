// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Stores the discovery state in the control plane database, shared by all replicas.</summary>
public sealed class DbDiscoveryStore : IDiscoveryStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="DbDiscoveryStore"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="time">The clock.</param>
    public DbDiscoveryStore(IServiceScopeFactory scopes, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(time);
        _scopes = scopes;
        _time = time;
    }

    /// <summary>Reads only the hash of the stored endpoints.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The hash, or an empty string when nothing is stored.</returns>
    public async Task<string> LoadEndpointsHashAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        return await db.DiscoveryStates.AsNoTracking().Select(d => d.EndpointsHash).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "";
    }

    /// <inheritdoc/>
    public async Task SaveAsync(DiscoveryState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hash = state.Endpoints.Hash();
        var json = GatewayJson.Serialize(Dto.From(state));
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var row = await db.DiscoveryStates.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is not null && row.StateJson == json)
        {
            return;
        }

        if (row is null)
        {
            row = new DiscoveryStateEntity();
            db.DiscoveryStates.Add(row);
        }

        row.StateJson = json;
        row.EndpointsHash = hash;
        row.UpdatedAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DiscoveryState> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var json = await db.DiscoveryStates.AsNoTracking().Select(d => d.StateJson).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return json is null ? DiscoveryState.Empty : GatewayJson.Deserialize<Dto>(json).ToState();
    }

    private sealed record Dto
    {
        public List<DiscoveredService> Services { get; init; } = [];

        public Dictionary<string, List<string>> Endpoints { get; init; } = [];

        public static Dto From(DiscoveryState state) => new()
        {
            Services = [.. state.Services],
            Endpoints = state.Endpoints.Addresses.ToDictionary(p => p.Key, p => p.Value.ToList()),
        };

        public DiscoveryState ToState() => new(
            Services,
            new ServiceEndpointSet(Endpoints.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value)));
    }
}
