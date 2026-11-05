// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Commits a configuration change atomically with its audit entry and the revision capturing the new draft.</summary>
public sealed class ChangeRecorder
{
    private readonly GatewrightDbContext _db;
    private readonly RevisionService _revisions;
    private readonly ActorAccessor _actor;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="ChangeRecorder"/> class.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="revisions">The revision service.</param>
    /// <param name="actor">Resolves who is calling.</param>
    /// <param name="time">The clock.</param>
    public ChangeRecorder(GatewrightDbContext db, RevisionService revisions, ActorAccessor actor, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _revisions = revisions;
        _actor = actor;
        _time = time;
    }

    /// <summary>Saves pending changes, records an audit entry and creates a draft revision, in one transaction.</summary>
    /// <param name="action">The action (for example <c>api.create</c>).</param>
    /// <param name="entityType">The kind of object changed.</param>
    /// <param name="entityId">The name or identifier of the object.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The revision that captures the change.</returns>
    public async Task<Revision> CommitAsync(string action, string entityType, string entityId, CancellationToken cancellationToken)
    {
        var actor = _actor.Current;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (revision, _) = await _revisions.CaptureDraftAsync(actor, $"{action} {entityId}", cancellationToken).ConfigureAwait(false);
        Audit(actor, action, entityType, entityId, $"revision {revision.Number}");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return revision;
    }

    /// <summary>Records an audit entry for an action that is not a draft change (publication, rollback…) and saves it.</summary>
    /// <param name="action">The action.</param>
    /// <param name="entityType">The kind of object.</param>
    /// <param name="entityId">The name or identifier.</param>
    /// <param name="details">A short description.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the entry is saved.</returns>
    public async Task AuditAsync(string action, string entityType, string entityId, string details, CancellationToken cancellationToken)
    {
        Audit(_actor.Current, action, entityType, entityId, details);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Audit(string actor, string action, string entityType, string entityId, string details) =>
        _db.AuditEntries.Add(new AuditEntry
        {
            Timestamp = _time.GetUtcNow(),
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
        });
}
