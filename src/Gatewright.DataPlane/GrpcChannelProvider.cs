// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Grpc.Net.Client;

namespace Gatewright.DataPlane;

/// <summary>Creates the gRPC channel toward the control plane. Replaceable in tests.</summary>
public interface IGrpcChannelProvider
{
    /// <summary>Creates a channel.</summary>
    /// <param name="endpoint">The control plane address.</param>
    /// <returns>The channel; the caller disposes it.</returns>
    GrpcChannel Create(Uri endpoint);
}

/// <summary>Default provider: a plain channel with HTTP/2 keep-alive pings so dead connections are noticed.</summary>
public sealed class DefaultGrpcChannelProvider : IGrpcChannelProvider
{
    /// <inheritdoc/>
    public GrpcChannel Create(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true,
        };
        return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true });
    }
}
