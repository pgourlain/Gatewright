// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net.WebSockets;
using System.Text;
using Gatewright.Core.Configuration;
using Gatewright.Distribution.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatewright.IntegrationTests;

public sealed class ProtocolPassthroughTests
{
    private sealed class EchoAckService : ConfigDistribution.ConfigDistributionBase
    {
        public override Task<AckResponse> Ack(AckRequest request, ServerCallContext context) => Task.FromResult(new AckResponse());

        public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
        {
            for (var i = 1; i <= 3; i++)
            {
                await responseStream.WriteAsync(new ServerMessage { Heartbeat = new Heartbeat { LatestVersion = i } });
            }
        }
    }

    [Fact]
    public async Task WebSocket_IsProxiedThroughTheGateway()
    {
        // YARP needs the HTTP upgrade feature that only a real server provides, so this test runs the gateway on Kestrel.
        await using var gateway = await TestGateway.StartAsync(url => new GatewayConfig { Apis = [TestGateway.Api(url)] }, useKestrel: true);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(gateway.GatewayUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/api/ws"), CancellationToken.None);

        await client.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[64];
        var result = await client.ReceiveAsync(buffer, CancellationToken.None);

        Encoding.UTF8.GetString(buffer, 0, result.Count).ShouldBe("ping");
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Grpc_UnaryAndServerStreaming_PassThroughOverHttp2()
    {
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        backendBuilder.Logging.ClearProviders();
        backendBuilder.Services.AddGrpc();
        await using var backend = backendBuilder.Build();
        backend.MapGrpcService<EchoAckService>();
        await backend.StartAsync();
        var backendUrl = backend.Urls.First();

        await using var gateway = await TestGateway.StartAsync(_ => new GatewayConfig
        {
            Apis =
            [
                new ApiDefinition
                {
                    Name = "grpc",
                    BasePath = "/",
                    Backend = new BackendDefinition { Destinations = [backendUrl], HttpVersion = "2" },
                },
            ],
        });
        using var channel = GrpcChannel.ForAddress(gateway.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = gateway.Server.CreateHandler() });
        var client = new ConfigDistribution.ConfigDistributionClient(channel);

        (await client.AckAsync(new AckRequest { NodeId = "n" })).ShouldNotBeNull();
        using var stream = client.Subscribe(new SubscribeRequest { NodeId = "n", Environment = "dev" });
        var versions = new List<long>();
        await foreach (var message in stream.ResponseStream.ReadAllAsync())
        {
            versions.Add(message.Heartbeat.LatestVersion);
        }

        versions.ShouldBe([1, 2, 3]);
    }
}
