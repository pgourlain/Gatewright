// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Gatewright.Core.OpenApi;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>What importing an OpenAPI document changed (or would change, for a dry run).</summary>
/// <param name="Api">The API name.</param>
/// <param name="Created">Whether the API did not exist before.</param>
/// <param name="Applied">Whether the draft was changed.</param>
/// <param name="ApiChanges">The API-level fields that changed (<c>basePath</c>, <c>version</c>, <c>backend</c>, <c>policies</c>, <c>products</c>, <c>displayName</c>).</param>
/// <param name="Added">The operations added.</param>
/// <param name="Updated">The operations modified.</param>
/// <param name="Removed">The operations removed because the document no longer declares them.</param>
/// <param name="Unchanged">The number of operations left as they were.</param>
/// <param name="Warnings">Things ignored or defaulted while reading the document.</param>
/// <param name="Errors">Why the document was refused.</param>
public sealed record ApiImportReport(
    string? Api,
    bool Created,
    bool Applied,
    IReadOnlyList<string> ApiChanges,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Removed,
    int Unchanged,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

/// <summary>Imports OpenAPI documents into the draft configuration as a new API or an update of an existing one.</summary>
public sealed class ApiImportService
{
    private readonly GatewrightDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="ApiImportService"/> class.</summary>
    /// <param name="db">The database context.</param>
    public ApiImportService(GatewrightDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>Reads a document and computes (and, unless dry run, applies) its effect on the draft. Saving and the revision are left to the caller.</summary>
    /// <param name="document">The OpenAPI document.</param>
    /// <param name="options">Import choices.</param>
    /// <param name="removeMissing">Whether operations missing from the document are removed from an existing API.</param>
    /// <param name="dryRun">Compute the report without changing anything.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The report.</returns>
    public async Task<ApiImportReport> ImportAsync(
        string document,
        OpenApiImportOptions options,
        bool removeMissing,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var read = OpenApiImporter.Import(document, options);
        if (!read.IsSuccess)
        {
            return new ApiImportReport(null, false, false, [], [], [], [], 0, read.Warnings, read.Errors);
        }

        var api = read.Api!;
        var products = await _db.Products.Where(p => api.Products.Contains(p.Name)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var missingProducts = api.Products.Except(products.Select(p => p.Name), StringComparer.Ordinal).ToList();
        if (missingProducts.Count > 0)
        {
            return new ApiImportReport(api.Name, false, false, [], [], [], [], 0, read.Warnings, [$"Product(s) do not exist: {string.Join(", ", missingProducts)}."]);
        }

        var existing = await _db.Apis.Include(a => a.Operations).Include(a => a.Products).AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Name == api.Name, cancellationToken).ConfigureAwait(false);
        var created = existing is null;
        var apiChanges = new List<string>();
        var added = new List<string>();
        var updated = new List<string>();
        var removed = new List<string>();
        var unchanged = 0;

        if (existing is null)
        {
            existing = new ApiEntity { Name = api.Name };
            if (!dryRun)
            {
                _db.Apis.Add(existing);
            }
        }

        Track(apiChanges, "displayName", existing.DisplayName, read.DisplayName, created);
        Track(apiChanges, "basePath", existing.BasePath, api.BasePath, created);
        Track(apiChanges, "version", existing.Version, api.Version, created);
        Track(apiChanges, "backend", GatewayJson.Serialize(existing.Backend), GatewayJson.Serialize(api.Backend), created);
        Track(apiChanges, "policies", GatewayJson.Serialize(existing.Policies), GatewayJson.Serialize(api.Policies), created);
        Track(
            apiChanges,
            "products",
            string.Join(',', existing.Products.Select(p => p.Name).Order(StringComparer.Ordinal)),
            string.Join(',', products.Select(p => p.Name).Order(StringComparer.Ordinal)),
            created);

        var current = existing.Operations.ToDictionary(o => o.Name, StringComparer.Ordinal);
        foreach (var operation in api.Operations)
        {
            if (!current.TryGetValue(operation.Name, out var entity))
            {
                added.Add($"{operation.Method} {operation.Path} ({operation.Name})");
                if (!dryRun)
                {
                    _db.Operations.Add(new OperationEntity
                    {
                        ApiId = existing.Id,
                        Name = operation.Name,
                        Method = operation.Method,
                        Path = operation.Path,
                        Policies = operation.Policies,
                    });
                }

                continue;
            }

            if (entity.Method == operation.Method && entity.Path == operation.Path
                && GatewayJson.Serialize(entity.Policies) == GatewayJson.Serialize(operation.Policies))
            {
                unchanged++;
                continue;
            }

            updated.Add($"{operation.Method} {operation.Path} ({operation.Name})");
            if (!dryRun)
            {
                entity.Method = operation.Method;
                entity.Path = operation.Path;
                entity.Policies = operation.Policies;
            }
        }

        if (removeMissing)
        {
            var declared = api.Operations.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var (name, entity) in current.Where(p => !declared.Contains(p.Key)))
            {
                removed.Add($"{entity.Method} {entity.Path} ({name})");
                if (!dryRun)
                {
                    _db.Operations.Remove(entity);
                }
            }
        }

        if (!dryRun)
        {
            existing.DisplayName = read.DisplayName;
            existing.BasePath = api.BasePath;
            existing.Version = api.Version;
            existing.Backend = api.Backend;
            existing.Policies = api.Policies;
            existing.Products.Clear();
            existing.Products.AddRange(products);
        }

        return new ApiImportReport(api.Name, created, !dryRun, apiChanges, added, updated, removed, unchanged, read.Warnings, []);
    }

    private static void Track(List<string> changes, string field, string? before, string? after, bool created)
    {
        if (created ? !string.IsNullOrEmpty(after) && after != "{}" : before != after)
        {
            changes.Add(field);
        }
    }
}
