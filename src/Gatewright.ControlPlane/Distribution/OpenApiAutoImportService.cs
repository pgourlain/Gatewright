// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Gatewright.Core.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>
/// On the leader only: when a discovered service is annotated with <c>gatewright.io/openapi-path</c>, fetches its OpenAPI document and creates or updates the API
/// in the draft (and optionally publishes it). A changed document yields a new draft revision; an unchanged one yields nothing.
/// </summary>
public sealed partial class OpenApiAutoImportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IDiscoveryStore _discovery;
    private readonly IOpenApiFetcher _fetcher;
    private readonly ILeaderElection? _leader;
    private readonly AutoImportOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OpenApiAutoImportService> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenApiAutoImportService"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="discovery">The discovery state store.</param>
    /// <param name="fetcher">Downloads documents.</param>
    /// <param name="options">The options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="leader">The leader election; without one (single replica) the service always runs.</param>
    public OpenApiAutoImportService(
        IServiceScopeFactory scopes,
        IDiscoveryStore discovery,
        IOpenApiFetcher fetcher,
        IOptions<AutoImportOptions> options,
        TimeProvider time,
        ILogger<OpenApiAutoImportService> logger,
        ILeaderElection? leader = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _discovery = discovery;
        _fetcher = fetcher;
        _leader = leader;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Checks every discovered service once.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The number of services whose API was created or updated.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var state = await _discovery.LoadAsync(cancellationToken).ConfigureAwait(false);
        var imported = 0;
        foreach (var service in state.Services.Where(s => s.OpenApiPath is not null && s.Ports.Count > 0))
        {
            var reference = service.Reference(service.Ports[0]);
            try
            {
                if (await ImportAsync(service, reference, cancellationToken).ConfigureAwait(false))
                {
                    imported++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(_logger, ex, reference);
                await RecordErrorAsync(reference, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        return imported;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_leader is null || _leader.IsLeader)
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogRunFailed(_logger, ex);
                    }
                }

                await Task.Delay(poll, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<bool> ImportAsync(DiscoveredService service, string reference, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            _options.UrlTemplate
                .Replace("{name}", service.Name, StringComparison.Ordinal)
                .Replace("{namespace}", service.Namespace, StringComparison.Ordinal)
                .Replace("{port}", service.Ports[0].ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .TrimEnd('/') + service.OpenApiPath);
        var document = await _fetcher.FetchAsync(uri, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reference + "\n" + (service.BasePath ?? "") + "\n" + (service.Product ?? "") + "\n" + document)));

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var record = await db.AutoImports.FirstOrDefaultAsync(r => r.ServiceRef == reference, cancellationToken).ConfigureAwait(false);
        if (record is not null && record.DocumentHash == hash && record.LastError is null)
        {
            return false;
        }

        scope.ServiceProvider.GetRequiredService<ActorAccessor>().Override = "system:auto-import";
        var importer = scope.ServiceProvider.GetRequiredService<ApiImportService>();
        var report = await importer.ImportAsync(
            document,
            new OpenApiImportOptions
            {
                Name = service.Name,
                BasePath = service.BasePath,
                Backend = new BackendDefinition { Service = reference },
                Products = service.Product is null ? null : [service.Product],
            },
            removeMissing: true,
            dryRun: false,
            cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            record = new AutoImportRecord { ServiceRef = reference };
            db.AutoImports.Add(record);
        }

        record.DocumentHash = hash;
        record.ImportedAt = _time.GetUtcNow();

        if (report.Errors.Count > 0)
        {
            record.LastError = string.Join("; ", report.Errors);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogRefused(_logger, reference, record.LastError);
            return false;
        }

        record.LastError = null;
        var revision = await scope.ServiceProvider.GetRequiredService<ChangeRecorder>().CommitAsync("api.auto-import", "api", report.Api!, cancellationToken).ConfigureAwait(false);
        var revisions = scope.ServiceProvider.GetRequiredService<RevisionService>();
        foreach (var environment in _options.PublishTo)
        {
            var result = await revisions.PublishAsync(revision.Number, environment, "system:auto-import", cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LogPublishRefused(_logger, reference, environment, string.Join("; ", result.Errors));
            }
        }

        LogImported(_logger, reference, report.Api!, report.Added.Count, report.Updated.Count, report.Removed.Count);
        return true;
    }

    private async Task RecordErrorAsync(string reference, string error, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
            var record = await db.AutoImports.FirstOrDefaultAsync(r => r.ServiceRef == reference, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                record = new AutoImportRecord { ServiceRef = reference };
                db.AutoImports.Add(record);
            }

            record.LastError = error;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Best effort: the next run retries and records again.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not import the OpenAPI document of {Service}; will retry.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string service);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic OpenAPI import run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The OpenAPI document of {Service} was refused: {Errors}")]
    private static partial void LogRefused(ILogger logger, string service, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Imported {Service} but publishing to {Environment} was refused: {Errors}")]
    private static partial void LogPublishRefused(ILogger logger, string service, string environment, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Service} as API {Api}: {Added} added, {Updated} updated, {Removed} removed operations.")]
    private static partial void LogImported(ILogger logger, string service, string api, int added, int updated, int removed);
}
