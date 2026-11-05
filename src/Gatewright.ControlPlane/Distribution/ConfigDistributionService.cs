// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Gatewright.Distribution;
using Gatewright.Distribution.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>
/// The gRPC distribution endpoint, served by every control plane replica: sends the active configuration of an environment when a node connects
/// (as a full snapshot, or as a delta when the node is only a version behind), then each new publication, with heartbeats while idle.
/// </summary>
public sealed partial class ConfigDistributionService : ConfigDistribution.ConfigDistributionBase
{
    private readonly PublicationHub _hub;
    private readonly EndpointsHub _endpoints;
    private readonly TlsSecretsHub _secrets;
    private readonly NodeRegistry _nodes;
    private readonly DistributionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ConfigDistributionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConfigDistributionService"/> class.</summary>
    /// <param name="hub">The publication hub.</param>
    /// <param name="endpoints">The endpoints hub.</param>
    /// <param name="secrets">The TLS secrets hub.</param>
    /// <param name="nodes">The node registry.</param>
    /// <param name="options">The distribution options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ConfigDistributionService(
        PublicationHub hub,
        EndpointsHub endpoints,
        TlsSecretsHub secrets,
        NodeRegistry nodes,
        IOptions<DistributionOptions> options,
        TimeProvider time,
        ILogger<ConfigDistributionService> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _hub = hub;
        _endpoints = endpoints;
        _secrets = secrets;
        _nodes = nodes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        Authorize(context);
        if (string.IsNullOrWhiteSpace(request.NodeId) || string.IsNullOrWhiteSpace(request.Environment))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "node_id and environment are required."));
        }

        var cancellationToken = context.CancellationToken;
        using var subscription = _hub.Subscribe(request.Environment);
        using var endpointSubscription = _endpoints.Subscribe();
        using var secretsSubscription = _secrets.Subscribe();
        await _nodes.ConnectedAsync(request.NodeId, request.Environment, request.LastVersion, request.LastHash, cancellationToken).ConfigureAwait(false);
        LogConnected(_logger, request.NodeId, request.Environment, request.LastVersion);
        try
        {
            var cursor = new Cursor(request.LastVersion);
            var current = await _hub.GetCurrentAsync(request.Environment, cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                if (request.LastVersion > 0 && request.LastVersion != current.Number)
                {
                    var known = await _hub.GetAsync(request.Environment, request.LastVersion, cancellationToken).ConfigureAwait(false);
                    if (known is not null && known.Hash == request.LastHash)
                    {
                        cursor.Config = GatewayConfigSerializer.Deserialize(known.ConfigJson);
                    }
                }

                if (!(request.LastVersion == current.Number && request.LastHash == current.Hash))
                {
                    await SendAsync(responseStream, current, cursor, cancellationToken).ConfigureAwait(false);
                }
            }

            var endpointsNow = await _endpoints.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (endpointsNow.Addresses.Count > 0)
            {
                await responseStream.WriteAsync(EndpointsMessage(endpointsNow), cancellationToken).ConfigureAwait(false);
            }

            var secretsNow = _secrets.GetCurrent();
            if (secretsNow.Secrets.Count > 0)
            {
                await responseStream.WriteAsync(SecretsMessage(secretsNow), cancellationToken).ConfigureAwait(false);
            }

            await StreamUpdatesAsync(request, responseStream, subscription, endpointSubscription, secretsSubscription, cursor, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The node disconnected or the server is shutting down.
        }
        finally
        {
            await _nodes.DisconnectedAsync(request.NodeId, request.Environment).ConfigureAwait(false);
            LogDisconnected(_logger, request.NodeId, request.Environment);
        }
    }

    /// <inheritdoc/>
    public override async Task<AckResponse> Ack(AckRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        Authorize(context);
        await _nodes.AckAsync(request.NodeId, request.Environment, request.Version, request.Hash, request.Applied, request.Error, context.CancellationToken).ConfigureAwait(false);
        return new AckResponse();
    }

    private async Task StreamUpdatesAsync(
        SubscribeRequest request,
        IServerStreamWriter<ServerMessage> stream,
        HubSubscription subscription,
        EndpointsSubscription endpointSubscription,
        TlsSecretsSubscription secretsSubscription,
        Cursor cursor,
        CancellationToken cancellationToken)
    {
        var heartbeat = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds));
        var waitToRead = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var waitEndpoints = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var waitSecrets = secretsSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timer = Task.Delay(heartbeat, _time, timeout.Token);
            var finished = await Task.WhenAny(waitToRead, waitEndpoints, waitSecrets, timer).ConfigureAwait(false);
            await timeout.CancelAsync().ConfigureAwait(false);

            if (finished == waitEndpoints)
            {
                if (!await waitEndpoints.ConfigureAwait(false))
                {
                    return;
                }

                while (endpointSubscription.Reader.TryRead(out var endpoints))
                {
                    await stream.WriteAsync(EndpointsMessage(endpoints), cancellationToken).ConfigureAwait(false);
                }

                waitEndpoints = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            else if (finished == waitSecrets)
            {
                if (!await waitSecrets.ConfigureAwait(false))
                {
                    return;
                }

                while (secretsSubscription.Reader.TryRead(out var secrets))
                {
                    await stream.WriteAsync(SecretsMessage(secrets), cancellationToken).ConfigureAwait(false);
                }

                waitSecrets = secretsSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            else if (finished == waitToRead)
            {
                if (!await waitToRead.ConfigureAwait(false))
                {
                    return;
                }

                while (subscription.Reader.TryRead(out var update))
                {
                    if (update.Number > cursor.Version)
                    {
                        await SendAsync(stream, update, cursor, cancellationToken).ConfigureAwait(false);
                    }
                }

                waitToRead = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            else
            {
                await stream.WriteAsync(new ServerMessage
                {
                    Heartbeat = new Heartbeat { ServerTimeUnixMs = _time.GetUtcNow().ToUnixTimeMilliseconds(), LatestVersion = cursor.Version },
                }, cancellationToken).ConfigureAwait(false);
                await _nodes.TouchAsync(request.NodeId, request.Environment, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendAsync(IServerStreamWriter<ServerMessage> stream, PublishedConfig published, Cursor cursor, CancellationToken cancellationToken)
    {
        var next = GatewayConfigSerializer.Deserialize(published.ConfigJson);
        ServerMessage message;
        if (cursor.Config is { } previous)
        {
            var delta = ConfigDeltaCalculator.Compute(previous, next);
            var deltaJson = Encoding.UTF8.GetBytes(GatewayJson.Serialize(delta));
            message = deltaJson.Length < published.ConfigJson.Length / 2
                ? new ServerMessage
                {
                    Delta = new Delta
                    {
                        FromVersion = cursor.Version,
                        ToVersion = published.Number,
                        ToHash = published.Hash,
                        DeltaJson = ByteString.CopyFrom(deltaJson),
                    },
                }
                : Snapshot(published);
        }
        else
        {
            message = Snapshot(published);
        }

        await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        cursor.Config = next;
        cursor.Version = published.Number;
    }

    private static ServerMessage EndpointsMessage(Gatewright.Core.Discovery.ServiceEndpointSet endpoints)
    {
        var update = new EndpointsUpdate { Hash = endpoints.Hash() };
        foreach (var (reference, addresses) in endpoints.Addresses)
        {
            var list = new EndpointList();
            list.Addresses.AddRange(addresses);
            update.Services[reference] = list;
        }

        return new ServerMessage { Endpoints = update };
    }

    private static ServerMessage SecretsMessage(CertificateMaterialSet secrets)
    {
        var update = new SecretsUpdate();
        foreach (var (reference, material) in secrets.Secrets)
        {
            update.Secrets[reference] = new SecretMaterial
            {
                CertificatePem = ByteString.CopyFromUtf8(material.CertificatePem),
                KeyPem = ByteString.CopyFromUtf8(material.KeyPem),
            };
        }

        return new ServerMessage { Secrets = update };
    }

    private static ServerMessage Snapshot(PublishedConfig published) => new()
    {
        Snapshot = new Snapshot
        {
            Version = published.Number,
            Hash = published.Hash,
            ConfigJson = ByteString.CopyFromUtf8(published.ConfigJson),
        },
    };

    private void Authorize(ServerCallContext context)
    {
        var header = context.RequestHeaders.GetValue("authorization") ?? "";
        const string prefix = "Bearer ";
        if (_options.Tokens.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "No distribution token is configured on this control plane."));
        }

        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "A bearer token is required."));
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()));
        var accepted = false;
        foreach (var token in _options.Tokens)
        {
            accepted |= CryptographicOperations.FixedTimeEquals(presented, SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        if (!accepted)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The token is not valid."));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Node {NodeId} connected to environment {Environment} at version {Version}.")]
    private static partial void LogConnected(ILogger logger, string nodeId, string environment, long version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Node {NodeId} disconnected from environment {Environment}.")]
    private static partial void LogDisconnected(ILogger logger, string nodeId, string environment);

    private sealed class Cursor(long version)
    {
        public long Version { get; set; } = version;

        public GatewayConfig? Config { get; set; }
    }
}
