// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Gatewright.Core.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>
/// Brings the database in line with the <c>HTTPRoute</c> objects of the cluster (ADR 0017): creates, updates and, with safeguards,
/// deletes the APIs whose source is Gateway API, in one revision per reconciliation. APIs declared by hand are never touched.
/// </summary>
public sealed partial class GatewayApiSynchronizer : IGatewayApiSink
{
    private const string Actor = "system:gatewayapi";

    private readonly IServiceScopeFactory _scopes;
    private readonly GatewayApiSyncOptions _options;
    private readonly ILogger<GatewayApiSynchronizer> _logger;

    /// <summary>Initializes a new instance of the <see cref="GatewayApiSynchronizer"/> class.</summary>
    /// <param name="scopes">Creates the scope of one reconciliation.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public GatewayApiSynchronizer(IServiceScopeFactory scopes, IOptions<GatewayApiSyncOptions> options, ILogger<GatewayApiSynchronizer> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GatewayApiRouteResult>> ApplyAsync(GatewayApiState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        scope.ServiceProvider.GetRequiredService<ActorAccessor>().Override = Actor;

        var all = await db.Apis.Include(a => a.Operations).AsSplitQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        var owned = all.Where(a => a.Source == ApiSource.GatewayApi && a.SourceRef is not null).ToDictionary(a => a.SourceRef!, StringComparer.Ordinal);
        var results = new List<GatewayApiRouteResult>();
        var changes = new List<string>();

        foreach (var route in state.Routes)
        {
            owned.TryGetValue(route.Ref, out var entity);
            if (entity is not null)
            {
                entity.SourceMissingCount = 0;
            }

            if (route.Api is null)
            {
                // The last good API keeps serving (D5); the problem is reported.
                results.Add(new GatewayApiRouteResult(route.Ref, false, "UnsupportedValue", string.Join(' ', route.Problems)));
                continue;
            }

            var conflict = FindConflict(all, entity, route.Api);
            if (conflict is not null)
            {
                results.Add(new GatewayApiRouteResult(route.Ref, false, "Conflict", $"The route conflicts with API '{conflict.Name}' (same name, or same hosts and base path)."));
                continue;
            }

            if (entity is null)
            {
                entity = Create(db, route);
                all.Add(entity);
                owned[route.Ref] = entity;
                changes.Add("+" + route.Ref);
            }
            else if (!(route.Generation is not null && entity.SourceGeneration == route.Generation && entity.SourceUid == route.Uid) && Update(db, entity, route))
            {
                changes.Add("~" + route.Ref);
            }

            entity.SourceUid = route.Uid;
            entity.SourceGeneration = route.Generation;
            results.Add(new GatewayApiRouteResult(route.Ref, true, "Accepted", "The route is applied."));
        }

        changes.AddRange(Delete(db, owned, state));

        if (changes.Count == 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return results;
        }

        var revision = await scope.ServiceProvider.GetRequiredService<ChangeRecorder>()
            .CommitAsync("api.gatewayapi-sync", "api", $"{changes.Count} route(s): {string.Join(' ', changes)}", cancellationToken).ConfigureAwait(false);
        var revisions = scope.ServiceProvider.GetRequiredService<RevisionService>();
        foreach (var environment in _options.PublishTo)
        {
            var published = await revisions.PublishAsync(revision.Number, environment, Actor, cancellationToken).ConfigureAwait(false);
            if (!published.IsSuccess)
            {
                LogPublishRefused(_logger, environment, string.Join("; ", published.Errors));
            }
        }

        LogSynchronized(_logger, changes.Count, revision.Number);
        return results;
    }

    private static ApiEntity? FindConflict(List<ApiEntity> all, ApiEntity? self, ApiDefinition api)
    {
        var key = GatewayConfigValidator.ConflictKey(api.BasePath, api.Hosts);
        return all.FirstOrDefault(a => a != self
            && (a.Name == api.Name || GatewayConfigValidator.ConflictKey(a.BasePath, a.Hosts) == key));
    }

    private static ApiEntity Create(GatewrightDbContext db, GatewayApiRoute route)
    {
        var api = route.Api!;
        var entity = new ApiEntity
        {
            Name = api.Name,
            BasePath = api.BasePath,
            Backend = api.Backend,
            Hosts = [.. api.Hosts ?? []],
            Source = ApiSource.GatewayApi,
            SourceRef = route.Ref,
            SourceProduct = api.Source!.Product,
        };
        db.Apis.Add(entity);
        foreach (var operation in api.Operations)
        {
            db.Operations.Add(new OperationEntity { ApiId = entity.Id, Name = operation.Name, Method = operation.Method, Path = operation.Path, Policies = operation.Policies });
        }

        return entity;
    }

    /// <summary>Applies the desired API to an existing entity and tells whether anything that reaches the configuration changed. Policies are never touched.</summary>
    private static bool Update(GatewrightDbContext db, ApiEntity entity, GatewayApiRoute route)
    {
        var api = route.Api!;
        var changed = false;
        if (entity.BasePath != api.BasePath)
        {
            entity.BasePath = api.BasePath;
            changed = true;
        }

        if (!entity.Hosts.SequenceEqual(api.Hosts ?? []))
        {
            entity.Hosts = [.. api.Hosts ?? []];
            changed = true;
        }

        if (GatewayJson.Serialize(entity.Backend) != GatewayJson.Serialize(api.Backend))
        {
            entity.Backend = api.Backend;
            changed = true;
        }

        if (entity.SourceProduct != api.Source!.Product)
        {
            entity.SourceProduct = api.Source.Product;
            changed = true;
        }

        var current = entity.Operations.ToDictionary(o => o.Name, StringComparer.Ordinal);
        foreach (var operation in api.Operations)
        {
            if (!current.Remove(operation.Name, out var existing))
            {
                db.Operations.Add(new OperationEntity { ApiId = entity.Id, Name = operation.Name, Method = operation.Method, Path = operation.Path, Policies = operation.Policies });
                changed = true;
            }
            else if (existing.Method != operation.Method || existing.Path != operation.Path)
            {
                existing.Method = operation.Method;
                existing.Path = operation.Path;
                changed = true;
            }
        }

        foreach (var removed in current.Values)
        {
            db.Operations.Remove(removed);
            changed = true;
        }

        return changed;
    }

    private IEnumerable<string> Delete(GatewrightDbContext db, Dictionary<string, ApiEntity> owned, GatewayApiState state)
    {
        var present = state.Routes.Select(r => r.Ref).ToHashSet(StringComparer.Ordinal);
        var due = new List<ApiEntity>();
        foreach (var (reference, entity) in owned)
        {
            if (present.Contains(reference))
            {
                continue;
            }

            entity.SourceMissingCount++;
            if (entity.SourceMissingCount >= Math.Max(1, _options.DeletionGraceReconciliations))
            {
                due.Add(entity);
            }
        }

        if (due.Count > Math.Max(0, _options.MaxDeletionsPerCycle))
        {
            LogDeletionCapReached(_logger, due.Count, _options.MaxDeletionsPerCycle);
            return [];
        }

        foreach (var entity in due)
        {
            db.Apis.Remove(entity);
        }

        return due.Select(e => "-" + e.SourceRef);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Gateway API routes synchronized: {Changes} change(s) in revision {Revision}.")]
    private static partial void LogSynchronized(ILogger logger, int changes, long revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Gateway API change was not published to {Environment}: {Errors}")]
    private static partial void LogPublishRefused(ILogger logger, string environment, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Due} Gateway API route(s) look deleted, more than the {Cap} allowed in one cycle: nothing is deleted. Check the cluster, or raise GatewayApi:MaxDeletionsPerCycle.")]
    private static partial void LogDeletionCapReached(ILogger logger, int due, int cap);
}
