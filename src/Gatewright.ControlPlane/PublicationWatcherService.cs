// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Distribution;
using Gatewright.Core.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatewright.ControlPlane;

/// <summary>
/// In single-process mode, forwards the active publication of an environment to the in-process distributor whenever it changes
/// (publication or rollback through the Admin API).
/// </summary>
public sealed partial class PublicationWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SnapshotTranslator _translator;
    private readonly IConfigDistributor _distributor;
    private readonly string _environment;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<PublicationWatcherService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PublicationWatcherService"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="translator">The snapshot translator.</param>
    /// <param name="distributor">The distributor.</param>
    /// <param name="environment">The environment whose publications are followed.</param>
    /// <param name="pollInterval">How often the database is checked.</param>
    /// <param name="logger">The logger.</param>
    public PublicationWatcherService(
        IServiceScopeFactory scopes,
        SnapshotTranslator translator,
        IConfigDistributor distributor,
        string environment,
        TimeSpan pollInterval,
        ILogger<PublicationWatcherService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(distributor);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _translator = translator;
        _distributor = distributor;
        _environment = environment;
        _pollInterval = pollInterval;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? lastPublication = null;
        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            do
            {
                try
                {
                    lastPublication = await PublishIfChangedAsync(lastPublication, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(_logger, ex, _environment);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<Guid?> PublishIfChangedAsync(Guid? last, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var revisions = scope.ServiceProvider.GetRequiredService<RevisionService>();

        // Idle polls cost one tiny query: the publication (with its snapshot JSON) is read only when its identifier changed.
        var currentId = await revisions.GetCurrentPublicationIdAsync(_environment, cancellationToken).ConfigureAwait(false);
        if (currentId is null || currentId == last)
        {
            return last;
        }

        var current = await revisions.GetCurrentAsync(_environment, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return last;
        }

        var result = _translator.Translate(GatewayConfigSerializer.Deserialize(current.SnapshotJson));
        if (!result.IsSuccess)
        {
            LogInvalid(_logger, current.Number, string.Join("; ", result.Errors));
            return current.Id;
        }

        await _distributor.PublishAsync(result.Snapshot!, cancellationToken).ConfigureAwait(false);
        return current.Id;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read the active publication of environment {Environment}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string environment);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publication {Number} cannot be applied by this gateway: {Errors}")]
    private static partial void LogInvalid(ILogger logger, long number, string errors);
}
