// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>Why a publication or rollback was refused.</summary>
public enum PublishFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>The revision does not exist.</summary>
    RevisionNotFound,

    /// <summary>The environment does not exist.</summary>
    EnvironmentNotFound,

    /// <summary>The configuration did not pass validation.</summary>
    Invalid,

    /// <summary>The revision was never published to this environment, so it cannot be restored.</summary>
    NeverPublished,
}

/// <summary>The outcome of a publication.</summary>
/// <param name="Publication">The new publication, or <see langword="null"/> when refused.</param>
/// <param name="Failure">Why it was refused.</param>
/// <param name="Errors">Validation errors, when <paramref name="Failure"/> is <see cref="PublishFailure.Invalid"/>.</param>
public sealed record PublishResult(Publication? Publication, PublishFailure Failure, IReadOnlyList<string> Errors)
{
    /// <summary>Gets a value indicating whether the publication succeeded.</summary>
    public bool IsSuccess => Publication is not null;
}

/// <summary>Creates revisions of the draft, publishes them to environments and rolls environments back.</summary>
public sealed class RevisionService
{
    private readonly GatewrightDbContext _db;
    private readonly ConfigExporter _exporter;
    private readonly SnapshotTranslator _translator;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="RevisionService"/> class.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="exporter">The draft exporter.</param>
    /// <param name="translator">Validates and compiles configurations before publication.</param>
    /// <param name="time">The clock.</param>
    public RevisionService(GatewrightDbContext db, ConfigExporter exporter, SnapshotTranslator translator, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _exporter = exporter;
        _translator = translator;
        _time = time;
    }

    /// <summary>
    /// Captures the current draft as a new revision. When the draft is identical to the latest revision, that revision is returned
    /// and nothing is created.
    /// </summary>
    /// <param name="actor">Who made the change.</param>
    /// <param name="summary">What changed, in one line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The revision and whether it is new.</returns>
    public async Task<(Revision Revision, bool Created)> CaptureDraftAsync(string actor, string summary, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var config = await _exporter.ExportAsync(cancellationToken).ConfigureAwait(false);
        var hash = ConfigHasher.Hash(config);

        var latest = await _db.Revisions.OrderByDescending(r => r.Number).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (latest is not null && latest.ConfigHash == hash)
        {
            return (latest, false);
        }

        var revision = new Revision
        {
            Number = (latest?.Number ?? 0) + 1,
            ConfigJson = GatewayConfigSerializer.Serialize(config),
            ConfigHash = hash,
            Summary = summary,
            CreatedBy = actor,
            CreatedAt = _time.GetUtcNow(),
        };
        _db.Revisions.Add(revision);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (revision, true);
    }

    /// <summary>Lists revisions, newest first.</summary>
    /// <param name="take">The maximum number of revisions.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The revisions.</returns>
    public async Task<IReadOnlyList<Revision>> ListAsync(int take, CancellationToken cancellationToken) =>
        await _db.Revisions.AsNoTracking().OrderByDescending(r => r.Number).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Gets a revision by number.</summary>
    /// <param name="number">The revision number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The revision, or <see langword="null"/>.</returns>
    public Task<Revision?> GetAsync(long number, CancellationToken cancellationToken) =>
        _db.Revisions.AsNoTracking().FirstOrDefaultAsync(r => r.Number == number, cancellationToken);

    /// <summary>Compares two revisions.</summary>
    /// <param name="fromNumber">The older revision number.</param>
    /// <param name="toNumber">The newer revision number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The differences, or <see langword="null"/> when a revision does not exist.</returns>
    public async Task<IReadOnlyList<DiffEntry>?> DiffAsync(long fromNumber, long toNumber, CancellationToken cancellationToken)
    {
        var from = await GetAsync(fromNumber, cancellationToken).ConfigureAwait(false);
        var to = await GetAsync(toNumber, cancellationToken).ConfigureAwait(false);
        if (from is null || to is null)
        {
            return null;
        }

        return ConfigDiff.Compare(GatewayConfigSerializer.Deserialize(from.ConfigJson), GatewayConfigSerializer.Deserialize(to.ConfigJson));
    }

    /// <summary>Publishes a revision to an environment after full validation. The revision becomes the active configuration of the environment.</summary>
    /// <param name="revisionNumber">The revision number.</param>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="actor">Who publishes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    public Task<PublishResult> PublishAsync(long revisionNumber, string environmentName, string actor, CancellationToken cancellationToken) =>
        ActivateAsync(revisionNumber, environmentName, actor, PublicationKind.Publish, cancellationToken);

    /// <summary>Restores a revision that was already published to the environment.</summary>
    /// <param name="revisionNumber">The revision number to restore.</param>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="actor">Who rolls back.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    public Task<PublishResult> RollbackAsync(long revisionNumber, string environmentName, string actor, CancellationToken cancellationToken) =>
        ActivateAsync(revisionNumber, environmentName, actor, PublicationKind.Rollback, cancellationToken);

    /// <summary>Gets the active publication of an environment.</summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The publication, or <see langword="null"/>.</returns>
    public async Task<Publication?> GetCurrentAsync(string environmentName, CancellationToken cancellationToken)
    {
        var environment = await _db.Environments.AsNoTracking().FirstOrDefaultAsync(e => e.Name == environmentName, cancellationToken).ConfigureAwait(false);
        if (environment?.CurrentPublicationId is not { } id)
        {
            return null;
        }

        return await _db.Publications.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads only the identifier of the active publication of an environment: one small query, used to detect changes cheaply.</summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identifier, or <see langword="null"/> when nothing is published.</returns>
    public async Task<Guid?> GetCurrentPublicationIdAsync(string environmentName, CancellationToken cancellationToken) =>
        await _db.Environments.AsNoTracking()
            .Where(e => e.Name == environmentName)
            .Select(e => e.CurrentPublicationId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the publications of an environment, newest first.</summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The publications.</returns>
    public async Task<IReadOnlyList<Publication>> ListPublicationsAsync(string environmentName, CancellationToken cancellationToken)
    {
        var environment = await _db.Environments.AsNoTracking().FirstOrDefaultAsync(e => e.Name == environmentName, cancellationToken).ConfigureAwait(false);
        if (environment is null)
        {
            return [];
        }

        return await _db.Publications.AsNoTracking().Where(p => p.EnvironmentId == environment.Id).OrderByDescending(p => p.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<PublishResult> ActivateAsync(
        long revisionNumber,
        string environmentName,
        string actor,
        PublicationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var environment = await _db.Environments.FirstOrDefaultAsync(e => e.Name == environmentName, cancellationToken).ConfigureAwait(false);
        if (environment is null)
        {
            return new PublishResult(null, PublishFailure.EnvironmentNotFound, []);
        }

        var revision = await _db.Revisions.FirstOrDefaultAsync(r => r.Number == revisionNumber, cancellationToken).ConfigureAwait(false);
        if (revision is null)
        {
            return new PublishResult(null, PublishFailure.RevisionNotFound, []);
        }

        if (kind == PublicationKind.Rollback
            && !await _db.Publications.AnyAsync(p => p.EnvironmentId == environment.Id && p.RevisionId == revision.Id, cancellationToken).ConfigureAwait(false))
        {
            return new PublishResult(null, PublishFailure.NeverPublished, []);
        }

        var lastNumber = await _db.Publications.Where(p => p.EnvironmentId == environment.Id)
            .Select(p => (long?)p.Number).MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0;
        var number = lastNumber + 1;

        var config = GatewayConfigSerializer.Deserialize(revision.ConfigJson) with { Version = number };
        var translation = _translator.Translate(config);
        if (!translation.IsSuccess)
        {
            return new PublishResult(null, PublishFailure.Invalid, translation.Errors);
        }

        var publication = new Publication
        {
            EnvironmentId = environment.Id,
            RevisionId = revision.Id,
            Number = number,
            Kind = kind,
            SnapshotJson = GatewayConfigSerializer.Serialize(config),
            SnapshotHash = translation.Snapshot!.Hash,
            PublishedBy = actor,
            PublishedAt = _time.GetUtcNow(),
        };
        _db.Publications.Add(publication);
        environment.CurrentPublicationId = publication.Id;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PublishResult(publication, PublishFailure.None, []);
    }
}
