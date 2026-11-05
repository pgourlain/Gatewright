// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>The outcome of an import.</summary>
/// <param name="Applied">Whether the draft was replaced.</param>
/// <param name="Errors">Why the document was refused.</param>
/// <param name="Hash">The content hash of the imported configuration, when valid.</param>
public sealed record ImportResult(bool Applied, IReadOnlyList<string> Errors, string? Hash);

/// <summary>Exports and imports whole environments as <see cref="DeclarativeExport"/> documents.</summary>
public sealed class ConfigPorter
{
    private readonly GatewrightDbContext _db;
    private readonly ConfigExporter _exporter;
    private readonly RevisionService _revisions;
    private readonly SnapshotTranslator _translator;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="ConfigPorter"/> class.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="exporter">The draft exporter.</param>
    /// <param name="revisions">The revision service.</param>
    /// <param name="translator">Validates imported configurations.</param>
    /// <param name="time">The clock.</param>
    public ConfigPorter(
        GatewrightDbContext db,
        ConfigExporter exporter,
        RevisionService revisions,
        SnapshotTranslator translator,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _exporter = exporter;
        _revisions = revisions;
        _translator = translator;
        _time = time;
    }

    /// <summary>Exports the draft, or the active publication of an environment.</summary>
    /// <param name="environmentName">The environment name, or <see langword="null"/> for the draft.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The document, or <see langword="null"/> when the environment has no publication.</returns>
    public async Task<DeclarativeExport?> ExportAsync(string? environmentName, CancellationToken cancellationToken)
    {
        GatewayConfig config;
        if (environmentName is null)
        {
            config = await _exporter.ExportAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var publication = await _revisions.GetCurrentAsync(environmentName, cancellationToken).ConfigureAwait(false);
            if (publication is null)
            {
                return null;
            }

            config = GatewayConfigSerializer.Deserialize(publication.SnapshotJson);
        }

        var apis = await _db.Apis.AsNoTracking().Where(a => a.DisplayName != null).ToListAsync(cancellationToken).ConfigureAwait(false);
        var products = await _db.Products.AsNoTracking().Include(p => p.Plans).ToListAsync(cancellationToken).ConfigureAwait(false);
        var consumers = await _db.Consumers.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var subscriptions = await _db.Subscriptions.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var consumerNames = consumers.ToDictionary(c => c.Id, c => c.Name);

        return new DeclarativeExport
        {
            Config = config,
            ApiDisplayNames = apis.OrderBy(a => a.Name, StringComparer.Ordinal).ToDictionary(a => a.Name, a => a.DisplayName!),
            Plans =
            [
                .. products.OrderBy(p => p.Name, StringComparer.Ordinal).SelectMany(p =>
                    p.Plans.OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new ExportedPlan(p.Name, x.Name, x.Description))),
            ],
            Consumers = [.. consumers.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => new ExportedConsumer(c.Name, c.Contact))],
            SubscriptionConsumers = subscriptions.OrderBy(s => s.Name, StringComparer.Ordinal).ToDictionary(s => s.Name, s => consumerNames[s.ConsumerId]),
        };
    }

    /// <summary>
    /// Replaces the whole draft with the content of a document. The configuration must be fully valid; nothing changes otherwise.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="dryRun">Validate only.</param>
    /// <param name="actor">Who imports.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The outcome.</returns>
    public async Task<ImportResult> ImportAsync(DeclarativeExport document, bool dryRun, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != DeclarativeExport.CurrentFormat)
        {
            return new ImportResult(false, [$"Unsupported format '{document.Format}'. Expected '{DeclarativeExport.CurrentFormat}'."], null);
        }

        var translation = _translator.Translate(document.Config);
        var errors = translation.Errors.ToList();
        ValidateCatalog(document, errors);
        if (errors.Count > 0)
        {
            return new ImportResult(false, errors, null);
        }

        var hash = translation.Snapshot!.Hash;
        if (dryRun)
        {
            return new ImportResult(false, [], hash);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ClearDraftAsync(cancellationToken).ConfigureAwait(false);
        BuildDraft(document);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (revision, _) = await _revisions.CaptureDraftAsync(actor, "import", cancellationToken).ConfigureAwait(false);
        _db.AuditEntries.Add(new AuditEntry
        {
            Timestamp = _time.GetUtcNow(),
            Actor = actor,
            Action = "import",
            EntityType = "configuration",
            EntityId = "draft",
            Details = $"revision {revision.Number}, hash {hash}",
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ImportResult(true, [], hash);
    }

    private static void ValidateCatalog(DeclarativeExport document, List<string> errors)
    {
        var consumers = document.Consumers.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var subscription in document.Config.Subscriptions)
        {
            if (!document.SubscriptionConsumers.TryGetValue(subscription.Id, out var consumer) || !consumers.Contains(consumer))
            {
                errors.Add($"[import.consumer] subscriptions[{subscription.Id}]: no known consumer is declared for this subscription.");
            }

            if (subscription.Plan is not null
                && !document.Plans.Any(p => p.Product == subscription.Product && p.Name == subscription.Plan))
            {
                errors.Add($"[import.plan] subscriptions[{subscription.Id}]: plan '{subscription.Plan}' is not declared in product '{subscription.Product}'.");
            }

            if (subscription.KeyHashes.Count > SubscriptionEntity.MaxActiveKeys)
            {
                errors.Add($"[import.keys] subscriptions[{subscription.Id}]: at most {SubscriptionEntity.MaxActiveKeys} keys are allowed.");
            }
        }

        foreach (var plan in document.Plans)
        {
            if (!document.Config.Products.Any(p => p.Name == plan.Product))
            {
                errors.Add($"[import.plan] plan '{plan.Name}': product '{plan.Product}' does not exist.");
            }
        }
    }

    private async Task ClearDraftAsync(CancellationToken cancellationToken)
    {
        _db.Subscriptions.RemoveRange(await _db.Subscriptions.ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Consumers.RemoveRange(await _db.Consumers.ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Apis.RemoveRange(await _db.Apis.ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Products.RemoveRange(await _db.Products.ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.SharedPolicies.RemoveRange(await _db.SharedPolicies.ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Hostnames.RemoveRange(await _db.Hostnames.ToListAsync(cancellationToken).ConfigureAwait(false));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void BuildDraft(DeclarativeExport document)
    {
        var config = document.Config;
        var scope = _db.ScopePolicies.Local.FirstOrDefault() ?? _db.ScopePolicies.FirstOrDefault();
        if (scope is null)
        {
            scope = new ScopePoliciesEntity();
            _db.ScopePolicies.Add(scope);
        }

        scope.Global = config.GlobalPolicies;
        scope.Gateway = config.GatewayPolicies;

        foreach (var hostname in config.Hostnames)
        {
            _db.Hostnames.Add(new HostnameEntity
            {
                Name = hostname.Name,
                Certificate = hostname.Certificate,
                RedirectHttpToHttps = hostname.RedirectHttpToHttps,
                Hsts = hostname.Hsts,
            });
        }

        foreach (var (name, definition) in config.Policies)
        {
            _db.SharedPolicies.Add(new SharedPolicyEntity { Name = name, Definition = definition });
        }

        var products = new Dictionary<string, ProductEntity>(StringComparer.Ordinal);
        foreach (var definition in config.Products)
        {
            var product = new ProductEntity { Name = definition.Name, RequiresSubscription = definition.RequiresSubscription, Policies = definition.Policies };
            products[definition.Name] = product;
            _db.Products.Add(product);
        }

        var plans = new Dictionary<(string, string), PlanEntity>();
        foreach (var plan in document.Plans)
        {
            var entity = new PlanEntity { ProductId = products[plan.Product].Id, Name = plan.Name, Description = plan.Description };
            plans[(plan.Product, plan.Name)] = entity;
            _db.Plans.Add(entity);
        }

        foreach (var definition in config.Apis)
        {
            var api = new ApiEntity
            {
                Name = definition.Name,
                DisplayName = document.ApiDisplayNames.GetValueOrDefault(definition.Name),
                BasePath = definition.BasePath,
                Version = definition.Version,
                Backend = definition.Backend,
                Policies = definition.Policies,
            };
            _db.Apis.Add(api);
            foreach (var productName in definition.Products)
            {
                api.Products.Add(products[productName]);
            }

            foreach (var operation in definition.Operations)
            {
                _db.Operations.Add(new OperationEntity
                {
                    ApiId = api.Id,
                    Name = operation.Name,
                    Method = operation.Method.ToUpperInvariant(),
                    Path = operation.Path,
                    Policies = operation.Policies,
                });
            }
        }

        var consumers = new Dictionary<string, ConsumerEntity>(StringComparer.Ordinal);
        foreach (var exported in document.Consumers)
        {
            var consumer = new ConsumerEntity { Name = exported.Name, Contact = exported.Contact };
            consumers[exported.Name] = consumer;
            _db.Consumers.Add(consumer);
        }

        var now = _time.GetUtcNow();
        foreach (var definition in config.Subscriptions)
        {
            var subscription = new SubscriptionEntity
            {
                Name = definition.Id,
                ConsumerId = consumers[document.SubscriptionConsumers[definition.Id]].Id,
                ProductId = products[definition.Product].Id,
                PlanId = definition.Plan is null ? null : plans[(definition.Product, definition.Plan)].Id,
                Active = definition.Active,
            };
            _db.Subscriptions.Add(subscription);
            var labels = new[] { KeyLabelsPrimary, KeyLabelsSecondary };
            for (var i = 0; i < definition.KeyHashes.Count; i++)
            {
                _db.SubscriptionKeys.Add(subscription.AddKey(definition.KeyHashes[i], labels[i], now));
            }
        }
    }

    private const string KeyLabelsPrimary = "primary";
    private const string KeyLabelsSecondary = "secondary";
}
