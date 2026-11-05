// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Distribution;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatewright.ControlPlane;

/// <summary>Watches a configuration source, translates each version to a snapshot and publishes it. Invalid configurations are logged and skipped.</summary>
public sealed partial class ConfigPublisherService : BackgroundService
{
    private readonly IConfigSource _source;
    private readonly SnapshotTranslator _translator;
    private readonly IConfigDistributor _distributor;
    private readonly ILogger<ConfigPublisherService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConfigPublisherService"/> class.</summary>
    /// <param name="source">The configuration source.</param>
    /// <param name="translator">The snapshot translator.</param>
    /// <param name="distributor">The distributor.</param>
    /// <param name="logger">The logger.</param>
    public ConfigPublisherService(
        IConfigSource source,
        SnapshotTranslator translator,
        IConfigDistributor distributor,
        ILogger<ConfigPublisherService> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(distributor);
        ArgumentNullException.ThrowIfNull(logger);
        _source = source;
        _translator = translator;
        _distributor = distributor;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var config in _source.WatchAsync(stoppingToken).ConfigureAwait(false))
            {
                var result = _translator.Translate(config);
                if (!result.IsSuccess)
                {
                    LogInvalid(_logger, config.Version, string.Join("; ", result.Errors));
                    continue;
                }

                await _distributor.PublishAsync(result.Snapshot!, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Configuration version {Version} is invalid and was not published: {Errors}")]
    private static partial void LogInvalid(ILogger logger, long version, string errors);
}
