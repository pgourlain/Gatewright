// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gatewright.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Gatewright.Core.Sources;

/// <summary>Reads the configuration from a JSON file, reloaded by polling when its content changes.</summary>
public sealed partial class FileConfigSource : IConfigSource
{
    private readonly string _path;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<FileConfigSource> _logger;
    private string? _lastError;

    /// <summary>Initializes a new instance of the <see cref="FileConfigSource"/> class.</summary>
    /// <param name="path">The JSON file path.</param>
    /// <param name="pollInterval">How often the file is checked.</param>
    /// <param name="logger">The logger.</param>
    public FileConfigSource(string path, TimeSpan pollInterval, ILogger<FileConfigSource> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), pollInterval, "Poll interval must be positive.");
        }

        _path = path;
        _pollInterval = pollInterval;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<GatewayConfig> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? lastHash = null;
        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            var config = await TryLoadAsync(lastHash, cancellationToken).ConfigureAwait(false);
            if (config is { } loaded)
            {
                lastHash = loaded.Hash;
                yield return loaded.Config;
            }
        }
        while (await WaitAsync(timer, cancellationToken).ConfigureAwait(false));
    }

    private static async ValueTask<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async ValueTask<(GatewayConfig Config, string Hash)?> TryLoadAsync(string? lastHash, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (hash == lastHash)
            {
                return null;
            }

            var config = GatewayConfigSerializer.Deserialize(Encoding.UTF8.GetString(bytes));
            _lastError = null;
            return (config, hash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (!string.Equals(_lastError, ex.Message, StringComparison.Ordinal))
            {
                _lastError = ex.Message;
                LogLoadFailed(_logger, _path, ex.Message);
            }

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot load configuration file {Path}, keeping the previous configuration: {Reason}")]
    private static partial void LogLoadFailed(ILogger logger, string path, string reason);
}
