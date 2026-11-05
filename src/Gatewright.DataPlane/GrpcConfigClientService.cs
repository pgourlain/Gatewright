// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using Gatewright.Core.Configuration;
using Gatewright.Core.Discovery;
using Gatewright.Core.Hosting;
using Gatewright.Core.Snapshots;
using Gatewright.Distribution;
using Gatewright.Distribution.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane;

/// <summary>
/// Keeps a stream open to the control plane and applies each configuration it receives. Reconnects with exponential backoff and jitter.
/// While the control plane is unreachable the node keeps serving its last known good configuration.
/// </summary>
public sealed partial class GrpcConfigClientService : BackgroundService
{
    private readonly DistributionOptions _options;
    private readonly GatewayOptions _gateway;
    private readonly SnapshotTranslator _translator;
    private readonly SnapshotApplier _applier;
    private readonly IGrpcChannelProvider _channels;
    private readonly ILogger<GrpcConfigClientService> _logger;
    private readonly string _nodeId;
    private GatewayConfig? _current;
    private long _version;
    private string _hash = "";

    /// <summary>Initializes a new instance of the <see cref="GrpcConfigClientService"/> class.</summary>
    /// <param name="options">The distribution options.</param>
    /// <param name="gateway">The gateway options.</param>
    /// <param name="translator">The snapshot translator.</param>
    /// <param name="applier">The snapshot applier.</param>
    /// <param name="channels">Creates the gRPC channel.</param>
    /// <param name="logger">The logger.</param>
    public GrpcConfigClientService(
        IOptions<DistributionOptions> options,
        IOptions<GatewayOptions> gateway,
        SnapshotTranslator translator,
        SnapshotApplier applier,
        IGrpcChannelProvider channels,
        ILogger<GrpcConfigClientService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _gateway = gateway.Value;
        _translator = translator;
        _applier = applier;
        _channels = channels;
        _logger = logger;
        _nodeId = _options.NodeId ?? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    /// <summary>Gets the identifier this node presents to the control plane.</summary>
    public string NodeId => _nodeId;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new Uri(_options.Endpoint ?? throw new InvalidOperationException("Distribution:Endpoint is not set."), UriKind.Absolute);
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var channel = _channels.Create(endpoint);
                var progressed = await RunOnceAsync(new ConfigDistribution.ConfigDistributionClient(channel), stoppingToken).ConfigureAwait(false);
                attempt = progressed ? 0 : attempt + 1;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                LogDisconnected(_logger, ex, endpoint.ToString());
            }

            try
            {
                await Task.Delay(Backoff(attempt), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(_options.MaxReconnectSeconds, Math.Pow(2, Math.Clamp(attempt, 0, 10)) / 2);
        return TimeSpan.FromSeconds(Math.Max(0.1, seconds) * (0.5 + (RandomNumberGenerator.GetInt32(0, 1000) / 2000.0)));
    }

    private async Task<bool> RunOnceAsync(ConfigDistribution.ConfigDistributionClient client, CancellationToken cancellationToken)
    {
        var headers = new Metadata { { "authorization", "Bearer " + _options.Token } };
        using var call = client.Subscribe(
            new SubscribeRequest
            {
                NodeId = _nodeId,
                Environment = _gateway.Environment,
                LastVersion = _version,
                LastHash = _hash,
                ProtocolVersion = 1,
            },
            headers,
            cancellationToken: cancellationToken);

        var progressed = false;
        var idleLimit = TimeSpan.FromSeconds(Math.Max(5, _options.HeartbeatSeconds * 3));
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idle.CancelAfter(idleLimit);
            bool hasMessage;
            try
            {
                hasMessage = await call.ResponseStream.MoveNext(idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("No message or heartbeat from the control plane; reconnecting.");
            }

            if (!hasMessage)
            {
                return progressed;
            }

            progressed = true;
            var message = call.ResponseStream.Current;
            switch (message.PayloadCase)
            {
                case ServerMessage.PayloadOneofCase.Snapshot:
                    await ApplyAsync(
                        client,
                        GatewayConfigSerializer.Deserialize(message.Snapshot.ConfigJson.ToStringUtf8()),
                        message.Snapshot.Version,
                        message.Snapshot.Hash,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case ServerMessage.PayloadOneofCase.Delta:
                    if (!await TryApplyDeltaAsync(client, message.Delta, cancellationToken).ConfigureAwait(false))
                    {
                        return progressed;
                    }

                    break;
                case ServerMessage.PayloadOneofCase.Endpoints:
                    _applier.ApplyEndpoints(new ServiceEndpointSet(
                        message.Endpoints.Services.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value.Addresses])));
                    break;
                case ServerMessage.PayloadOneofCase.Secrets:
                    foreach (var (reference, material) in message.Secrets.Secrets)
                    {
                        _applier.ApplySecret(reference, material.CertificatePem.ToStringUtf8(), material.KeyPem.ToStringUtf8());
                    }

                    break;
                case ServerMessage.PayloadOneofCase.Heartbeat:
                    break;
            }
        }
    }

    private async Task<bool> TryApplyDeltaAsync(ConfigDistribution.ConfigDistributionClient client, Delta delta, CancellationToken cancellationToken)
    {
        if (_current is null || delta.FromVersion != _version)
        {
            LogResync(_logger, delta.FromVersion, _version);
            ResetState();
            return false;
        }

        var config = ConfigDeltaCalculator.Apply(_current, GatewayJson.Deserialize<ConfigDelta>(delta.DeltaJson.ToStringUtf8()), delta.ToVersion);
        if (ConfigHasher.Hash(config) != delta.ToHash)
        {
            LogResync(_logger, delta.FromVersion, _version);
            ResetState();
            return false;
        }

        await ApplyAsync(client, config, delta.ToVersion, delta.ToHash, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ApplyAsync(
        ConfigDistribution.ConfigDistributionClient client,
        GatewayConfig config,
        long version,
        string hash,
        CancellationToken cancellationToken)
    {
        var translation = _translator.Translate(config with { Version = version });
        var applied = false;
        string? error = null;
        if (!translation.IsSuccess)
        {
            error = string.Join("; ", translation.Errors);
        }
        else
        {
            applied = await _applier.ApplyAsync(translation.Snapshot!).ConfigureAwait(false);
            error = applied ? null : "The proxy rejected the configuration.";
        }

        if (applied)
        {
            _current = config;
            _version = version;
            _hash = hash;
            LogApplied(_logger, version);
        }
        else
        {
            LogRefused(_logger, version, error ?? "unknown");
        }

        try
        {
            await client.AckAsync(
                new AckRequest
                {
                    NodeId = _nodeId,
                    Environment = _gateway.Environment,
                    Version = version,
                    Hash = hash,
                    Applied = applied,
                    Error = error ?? string.Empty,
                },
                new Metadata { { "authorization", "Bearer " + _options.Token } },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            LogAckFailed(_logger, ex, version);
        }
    }

    private void ResetState()
    {
        _current = null;
        _version = 0;
        _hash = "";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied configuration version {Version} received from the control plane.")]
    private static partial void LogApplied(ILogger logger, long version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Configuration version {Version} refused, keeping the previous one: {Error}")]
    private static partial void LogRefused(ILogger logger, long version, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delta from version {From} does not apply to version {Current}; asking for a full snapshot.")]
    private static partial void LogResync(ILogger logger, long from, long current);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnected from the control plane at {Endpoint}; the last known good configuration keeps serving. Reconnecting.")]
    private static partial void LogDisconnected(ILogger logger, Exception exception, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not acknowledge version {Version} to the control plane.")]
    private static partial void LogAckFailed(ILogger logger, Exception exception, long version);
}
